use std::collections::HashMap;
use std::io;
use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc, OnceLock,
};
use std::time::{Duration, Instant};

use bytes::BytesMut;
use packet_queue::PacketPipe;
use policy::{Config, Direction, Domain, PolicyState, SessionGeneration, TrafficClass};
use tcp_ip::tcp::TcpListener as StackTcpListener;
use tcp_ip::udp::UdpSocket as StackUdpSocket;
use tcp_ip::{ip_stack, IpStackConfig, IpStackRecv};
use telemetry::Telemetry;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream, UdpSocket};
use tokio::sync::mpsc;
use tokio::sync::Semaphore;

mod flow_scheduler;
mod packet_queue;
mod policy;
mod proxy_copy;
#[cfg(test)]
mod qos_tests;
mod rate_limit;
mod telemetry;

const PORT: u16 = 27184;
const MTU: usize = 1500;
const UDP_IDLE: Duration = Duration::from_secs(60);
const SPEED_TEST_PORT: u16 = 28085;
const LINK_PROTOCOL: &str = "NOVORA-LINK";
const LINK_VERSION: u8 = 2;
const DATA_AUTH_ACK: &str = "NOVORA-LINK|2|DATA|ACK";
const AUTH_MAX_FRAME: usize = 1024;
const AUTH_TIMEOUT: Duration = Duration::from_secs(5);
const MAX_PENDING_AUTH: usize = 4;
const TOKEN_ENV: &str = "NOVORA_RELAY_TOKEN";

fn udp_activity_millis() -> u64 {
    static ORIGIN: OnceLock<Instant> = OnceLock::new();
    ORIGIN
        .get_or_init(Instant::now)
        .elapsed()
        .as_millis()
        .min(u64::MAX as u128) as u64
}

fn udp_flow_is_idle(last_activity_ms: u64, now_ms: u64) -> bool {
    now_ms.saturating_sub(last_activity_ms) >= UDP_IDLE.as_millis() as u64
}

#[derive(Default)]
struct Traffic {
    tx_bytes: AtomicU64,
    rx_bytes: AtomicU64,
    tx_packets: AtomicU64,
    rx_packets: AtomicU64,
}

#[tokio::main]
async fn main() -> io::Result<()> {
    let authentication_token = std::env::var(TOKEN_ENV).map_err(|_| {
        io::Error::new(
            io::ErrorKind::PermissionDenied,
            format!("{TOKEN_ENV} is required"),
        )
    })?;
    if !is_valid_token(&authentication_token) {
        return Err(io::Error::new(
            io::ErrorKind::PermissionDenied,
            "invalid relay authentication token",
        ));
    }
    let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, PORT)).await?;
    println!("NOVORA LinkEngine Relay DATA tcp:{PORT}");
    let policy = Arc::new(PolicyState::new());
    println!("LE_READY {{\"v\":1,\"shaping\":true,\"fairQueues\":true,\"queueTelemetry\":true}}");
    let stdin_policy = policy.clone();
    let stdin = tokio::spawn(async move { configuration_loop(stdin_policy).await });
    let result = run_with_policy(listener, policy, Arc::new(authentication_token)).await;
    stdin.abort();
    result
}

#[cfg(test)]
const TEST_TOKEN: &str = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

#[cfg(test)]
async fn run(listener: TcpListener) -> io::Result<()> {
    run_with_policy(
        listener,
        Arc::new(PolicyState::new()),
        Arc::new(TEST_TOKEN.to_string()),
    )
    .await
}

async fn configuration_loop(policy: Arc<PolicyState>) {
    let mut input = tokio::io::BufReader::new(tokio::io::stdin());
    loop {
        let line = match policy::read_line_bounded(&mut input).await {
            Ok(Some(line)) => line,
            Ok(None) => break,
            Err(error) => {
                emit_ack(policy.rejected(error.to_string()));
                if error.kind() != io::ErrorKind::InvalidData
                    || error.to_string() == "unterminated configuration line"
                {
                    break;
                }
                continue;
            }
        };
        let ack = if let Some(json) = line.strip_prefix(b"LE_CONFIG ") {
            match Config::parse(json) {
                Ok(config) => policy.apply(config),
                Err(error) => policy.rejected(error.to_string()),
            }
        } else {
            policy.rejected("expected LE_CONFIG JSON".into())
        };
        emit_ack(ack);
    }
    // EOF never removes the last accepted manual policy.
}

fn emit_ack(ack: policy::Ack) {
    if let Ok(json) = serde_json::to_string(&ack) {
        if json.len() + "LE_CONFIG_ACK ".len() <= policy::MAX_LINE {
            println!("LE_CONFIG_ACK {json}");
        }
    }
}

async fn run_with_policy(
    listener: TcpListener,
    policy: Arc<PolicyState>,
    authentication_token: Arc<String>,
) -> io::Result<()> {
    let mut active: Option<tokio::task::JoinHandle<()>> = None;
    let pending_auth = Arc::new(Semaphore::new(MAX_PENDING_AUTH));
    let (ready_tx, mut ready_rx) = mpsc::channel::<TcpStream>(MAX_PENDING_AUTH);

    loop {
        tokio::select! {
            accepted = listener.accept() => {
                let (mut stream, _) = accepted?;
                stream.set_nodelay(true)?;
                let permit = match pending_auth.clone().try_acquire_owned() {
                    Ok(permit) => permit,
                    Err(_) => {
                        // Do not let unauthenticated local connections exhaust the listener.
                        continue;
                    }
                };
                let expected = authentication_token.clone();
                let ready = ready_tx.clone();
                tokio::spawn(async move {
                    let _permit = permit;
                    let authenticated = tokio::time::timeout(
                        AUTH_TIMEOUT,
                        authenticate(&mut stream, expected.as_str()),
                    )
                    .await;
                    if matches!(authenticated, Ok(Ok(()))) {
                        if write_frame(&mut stream, DATA_AUTH_ACK).await.is_ok() {
                            let _ = ready.send(stream).await;
                        }
                    }
                });
            }
            Some(stream) = ready_rx.recv() => {
                // A session only replaces the active one after proving the token.
                if let Some(previous) = active.take() {
                    previous.abort();
                    let _ = previous.await;
                }
                let session_policy = policy.clone();
                active = Some(tokio::spawn(async move {
                    if let Err(error) = serve(stream, session_policy).await {
                        eprintln!("WARN NOVORA relay session: {error}");
                    }
                }));
            }
        }
    }
}

async fn authenticate(stream: &mut TcpStream, expected_token: &str) -> io::Result<()> {
    let payload = read_frame(stream).await?;
    let expected = format!("{LINK_PROTOCOL}|{LINK_VERSION}|DATA|{expected_token}");
    if !constant_time_eq(payload.as_bytes(), expected.as_bytes()) {
        return Err(io::Error::new(
            io::ErrorKind::PermissionDenied,
            "invalid DATA authentication token",
        ));
    }
    Ok(())
}

async fn read_frame(stream: &mut TcpStream) -> io::Result<String> {
    let mut header = [0_u8; 4];
    stream.read_exact(&mut header).await?;
    let length = u32::from_be_bytes(header) as usize;
    if length == 0 || length > AUTH_MAX_FRAME {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "invalid authentication frame length",
        ));
    }
    let mut payload = vec![0_u8; length];
    stream.read_exact(&mut payload).await?;
    String::from_utf8(payload).map_err(|_| {
        io::Error::new(
            io::ErrorKind::InvalidData,
            "authentication frame is not UTF-8",
        )
    })
}

async fn write_frame(stream: &mut TcpStream, payload: &str) -> io::Result<()> {
    if payload.is_empty() || payload.len() > AUTH_MAX_FRAME {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "authentication response is out of range",
        ));
    }
    stream
        .write_all(&(payload.len() as u32).to_be_bytes())
        .await?;
    stream.write_all(payload.as_bytes()).await?;
    stream.flush().await
}

fn is_valid_token(token: &str) -> bool {
    token.len() == 64 && token.bytes().all(|byte| byte.is_ascii_hexdigit())
}

fn constant_time_eq(left: &[u8], right: &[u8]) -> bool {
    if left.len() != right.len() {
        return false;
    }
    let mut difference = 0_u8;
    for (left_byte, right_byte) in left.iter().zip(right) {
        difference |= left_byte ^ right_byte;
    }
    difference == 0
}

async fn serve(mut stream: TcpStream, policy: Arc<PolicyState>) -> io::Result<()> {
    let generation = policy.bind_session();
    // Preserve the AppControl DATA contract: a four-byte network-order ID,
    // followed by contiguous IPv4 packets in both directions.
    stream.write_all(&1_u32.to_be_bytes()).await?;

    let config = IpStackConfig::builder().ipv4_mtu(MTU as u16).build();
    let (stack, stack_send, stack_recv) = ip_stack(config)?;
    let mut tcp_listener = StackTcpListener::bind_all(stack.clone()).await?;
    let udp_socket = StackUdpSocket::bind_all(stack).await?;
    let (mut reader, mut writer) = stream.into_split();
    let traffic = Arc::new(Traffic::default());
    let telemetry = Arc::new(Telemetry::new());
    let memory = Arc::new(Semaphore::new(flow_scheduler::MAX_BYTES));
    let flow_slots = Arc::new(Semaphore::new(flow_scheduler::MAX_FLOWS));
    let to_stack = Arc::new(PacketPipe::new(memory.clone(), Direction::FromAndroid));
    let to_network = Arc::new(PacketPipe::new(memory.clone(), Direction::ToAndroid));
    let measured_input = to_stack.clone();
    let measured_output = to_network.clone();
    let outbound_traffic = traffic.clone();
    let inbound_traffic = traffic.clone();
    let input_policy = policy.clone();
    let input_telemetry = telemetry.clone();
    let input_queue = to_stack.clone();
    let network_to_stack = async move {
        let mut packet = [0_u8; 65535];
        loop {
            reader.read_exact(&mut packet[..20]).await?;
            let length = u16::from_be_bytes([packet[2], packet[3]]) as usize;
            let header = ((packet[0] & 15) as usize) * 4;
            if packet[0] >> 4 != 4 || header < 20 || length < header || length > MTU {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidData,
                    "invalid IPv4 DATA packet",
                ));
            }
            reader.read_exact(&mut packet[20..length]).await?;
            input_queue
                .enqueue(packet[..length].to_vec(), &input_policy, &input_telemetry)
                .await?;
        }
    };
    let dispatch_policy = policy.clone();
    let dispatch_telemetry = telemetry.clone();
    let dispatch_generation = generation.clone();
    let dispatch_to_stack = async move {
        let mut changed = dispatch_policy.subscribe();
        loop {
            let packet = to_stack
                .dequeue(&dispatch_policy, &dispatch_telemetry)
                .await;
            wait_packet(
                &dispatch_policy,
                Direction::FromAndroid,
                packet.bytes.len(),
                &dispatch_generation,
                &mut changed,
            )
            .await?;
            let start = Instant::now();
            stack_send.send_ip_packet(&packet.bytes).await?;
            dispatch_telemetry.record_wait(
                Domain::DeviceLink,
                Direction::FromAndroid,
                start.elapsed(),
            );
            record_ip_bytes(&dispatch_telemetry, Direction::FromAndroid, &packet.bytes);
            outbound_traffic
                .tx_bytes
                .fetch_add(packet.bytes.len() as u64, Ordering::Relaxed);
            outbound_traffic.tx_packets.fetch_add(1, Ordering::Relaxed);
        }
        #[allow(unreachable_code)]
        Ok::<(), io::Error>(())
    };
    let output_policy = policy.clone();
    let output_telemetry = telemetry.clone();
    let output_queue = to_network.clone();
    let stack_to_network = async move {
        relay_stack_output(stack_recv, output_queue, output_policy, output_telemetry).await
    };
    let writer_policy = policy.clone();
    let writer_telemetry = telemetry.clone();
    let writer_generation = generation.clone();
    let write_to_network = async move {
        let mut changed = writer_policy.subscribe();
        loop {
            let packet = to_network.dequeue(&writer_policy, &writer_telemetry).await;
            let mut offset = 0;
            while offset < packet.bytes.len() {
                let length = writer_policy
                    .chunk_size(
                        Domain::DeviceLink,
                        Direction::ToAndroid,
                        TrafficClass::BestEffort,
                    )
                    .min(packet.bytes.len() - offset);
                writer_policy
                    .wait_session(
                        Domain::DeviceLink,
                        Direction::ToAndroid,
                        TrafficClass::BestEffort,
                        length,
                        &writer_generation,
                        &mut changed,
                    )
                    .await?;
                let start = Instant::now();
                writer
                    .write_all(&packet.bytes[offset..offset + length])
                    .await?;
                writer_telemetry.record_wait(
                    Domain::DeviceLink,
                    Direction::ToAndroid,
                    start.elapsed(),
                );
                offset += length;
            }
            record_ip_bytes(&writer_telemetry, Direction::ToAndroid, &packet.bytes);
            inbound_traffic
                .rx_bytes
                .fetch_add(packet.bytes.len() as u64, Ordering::Relaxed);
            inbound_traffic.rx_packets.fetch_add(1, Ordering::Relaxed);
        }
        #[allow(unreachable_code)]
        Ok::<(), io::Error>(())
    };
    let tcp_policy = policy.clone();
    let tcp_telemetry = telemetry.clone();
    let tcp_slots = flow_slots.clone();
    let tcp_generation = generation.clone();
    let tcp_proxy = async move {
        let mut tasks = tokio::task::JoinSet::new();
        loop {
            let (mut client, _) = tcp_listener.accept().await?;
            while tasks.try_join_next().is_some() {}
            let destination = client.local_addr()?;
            if !is_forwardable_tcp(destination) {
                continue;
            }
            let slot = match tcp_slots.clone().try_acquire_owned() {
                Ok(slot) => slot,
                Err(_) => {
                    tcp_telemetry.record_drop_at(
                        Domain::Internet,
                        Direction::FromAndroid,
                        "flow_limit",
                    );
                    continue;
                }
            };
            let connection_policy = tcp_policy.clone();
            let connection_telemetry = tcp_telemetry.clone();
            let connection_generation = tcp_generation.clone();
            tasks.spawn(async move {
                let _slot = slot;
                if let Ok(mut upstream) = TcpStream::connect(tcp_upstream(destination)).await {
                    // Relayed packets are forwarded as they arrive; never hold small writes for Nagle.
                    let _ = upstream.set_nodelay(true);
                    let internet = tcp_upstream(destination) == destination;
                    let _ = proxy_copy::copy_proxy(
                        &mut client,
                        &mut upstream,
                        connection_policy,
                        connection_telemetry,
                        connection_generation,
                        internet,
                    )
                    .await;
                }
            });
        }
        #[allow(unreachable_code)]
        Ok::<(), io::Error>(())
    };

    let udp_policy = policy.clone();
    let udp_telemetry = telemetry.clone();
    let udp_generation = generation.clone();
    let udp_proxy = async move {
        relay_udp(
            udp_socket,
            udp_policy,
            udp_telemetry,
            memory,
            flow_slots,
            udp_generation,
        )
        .await
    };
    let metrics = report_traffic(
        traffic,
        telemetry,
        policy.clone(),
        measured_input,
        measured_output,
        generation.clone(),
    );
    let mut changed = policy.subscribe();
    let replaced = async move {
        loop {
            if let Err(error) = policy.session_snapshot(&generation) {
                return error;
            }
            if changed.changed().await.is_err() {
                return policy::interrupted();
            }
        }
    };

    tokio::select! {
        result = network_to_stack => result,
        result = dispatch_to_stack => result,
        result = stack_to_network => result,
        result = write_to_network => result,
        result = tcp_proxy => result,
        result = udp_proxy => result,
        result = metrics => result,
        error = replaced => Err(error),
    }
}

async fn relay_stack_output(
    mut stack_recv: IpStackRecv,
    queue: Arc<PacketPipe>,
    policy: Arc<PolicyState>,
    telemetry: Arc<Telemetry>,
) -> io::Result<()> {
    let mut buffers: Vec<BytesMut> = (0..32).map(|_| BytesMut::zeroed(MTU)).collect();
    let mut sizes = vec![0_usize; buffers.len()];
    loop {
        let count = stack_recv.recv_ip_packet(&mut buffers, &mut sizes).await?;
        for index in 0..count {
            if sizes[index] > MTU {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidData,
                    "stack packet exceeds MTU",
                ));
            }
            queue
                .enqueue(buffers[index][..sizes[index]].to_vec(), &policy, &telemetry)
                .await?;
        }
    }
}

async fn report_traffic(
    traffic: Arc<Traffic>,
    telemetry: Arc<Telemetry>,
    policy: Arc<PolicyState>,
    input: Arc<PacketPipe>,
    output: Arc<PacketPipe>,
    generation: Arc<SessionGeneration>,
) -> io::Result<()> {
    let mut last = Instant::now();
    let mut sequence = 0u64;
    loop {
        tokio::time::sleep(Duration::from_secs(1)).await;
        println!(
            "LE_METRIC tx={} rx={} txp={} rxp={}",
            traffic.tx_bytes.load(Ordering::Relaxed),
            traffic.rx_bytes.load(Ordering::Relaxed),
            traffic.tx_packets.load(Ordering::Relaxed),
            traffic.rx_packets.load(Ordering::Relaxed)
        );
        let config = policy.session_snapshot(&generation)?;
        sequence += 1;
        let elapsed = last.elapsed();
        last = Instant::now();
        input.sample(&policy, &telemetry);
        output.sample(&policy, &telemetry);
        let sample = telemetry.snapshot(&config, sequence, elapsed);
        if config.generation > 0 {
            if let Ok(json) = serde_json::to_string(&sample) {
                if json.len() + "LE_QOS ".len() <= policy::MAX_LINE {
                    println!("LE_QOS {json}");
                }
            }
        }
    }
}

async fn wait_packet(
    policy: &PolicyState,
    direction: Direction,
    bytes: usize,
    generation: &SessionGeneration,
    changed: &mut tokio::sync::watch::Receiver<policy::Epoch>,
) -> io::Result<()> {
    let mut remaining = bytes;
    while remaining > 0 {
        let chunk = remaining.min(policy.chunk_size(
            Domain::DeviceLink,
            direction,
            TrafficClass::BestEffort,
        ));
        policy
            .wait_session(
                Domain::DeviceLink,
                direction,
                TrafficClass::BestEffort,
                chunk,
                generation,
                changed,
            )
            .await?;
        remaining -= chunk;
    }
    Ok(())
}

fn record_ip_bytes(telemetry: &Telemetry, direction: Direction, packet: &[u8]) {
    let header = (packet[0] as usize & 15) * 4;
    let offset = u16::from_be_bytes([packet[6], packet[7]]) & 0x1fff;
    let transport = match packet[9] {
        6 if offset == 0 && packet.len() >= header + 20 => {
            let length = (packet[header + 12] as usize >> 4) * 4;
            if length >= 20 && length <= packet.len() - header {
                length
            } else {
                0
            }
        }
        17 if offset == 0 && packet.len() >= header + 8 => 8,
        _ => 0,
    };
    let protocol = (header + transport).min(packet.len());
    telemetry.record_bytes(
        Domain::DeviceLink,
        direction,
        (packet.len() - protocol) as u64,
        protocol as u64,
    );
}

struct UdpPayload {
    bytes: Vec<u8>,
    _memory: tokio::sync::OwnedSemaphorePermit,
    _flow_memory: tokio::sync::OwnedSemaphorePermit,
}

async fn relay_udp(
    socket: StackUdpSocket,
    policy: Arc<PolicyState>,
    telemetry: Arc<Telemetry>,
    memory: Arc<Semaphore>,
    slots: Arc<Semaphore>,
    generation: Arc<SessionGeneration>,
) -> io::Result<()> {
    let socket = std::sync::Arc::new(socket);
    let mut tasks = tokio::task::JoinSet::new();
    let mut flows: HashMap<
        (SocketAddr, SocketAddr),
        (mpsc::Sender<UdpPayload>, Arc<AtomicU64>, Arc<Semaphore>),
    > = HashMap::new();
    let mut packet = [0_u8; 65535];
    loop {
        let (count, source, destination) = socket.recv_from_to(&mut packet).await?;
        while tasks.try_join_next().is_some() {}
        if !is_forwardable(destination) {
            continue;
        }
        let now_ms = udp_activity_millis();
        flows.retain(|_, (sender, last, _)| {
            !sender.is_closed() && !udp_flow_is_idle(last.load(Ordering::Relaxed), now_ms)
        });
        let key = (source, destination);
        if !flows.contains_key(&key) {
            let slot = match slots.clone().try_acquire_owned() {
                Ok(slot) => slot,
                Err(_) => {
                    telemetry.record_drop_at(
                        Domain::Internet,
                        Direction::FromAndroid,
                        "flow_limit",
                    );
                    continue;
                }
            };
            let upstream = UdpSocket::bind((Ipv4Addr::UNSPECIFIED, 0)).await?;
            upstream.connect(destination).await?;
            let (sender, mut receiver) = mpsc::channel::<UdpPayload>(64);
            let flow_memory = Arc::new(Semaphore::new(flow_scheduler::MAX_FLOW_BYTES));
            let last_activity = Arc::new(AtomicU64::new(now_ms));
            let incoming_activity = last_activity.clone();
            let replies = socket.clone();
            let flow_policy = policy.clone();
            let flow_telemetry = telemetry.clone();
            let flow_generation = generation.clone();
            tasks.spawn(async move {
                let _slot = slot;
                let mut outgoing_changed = flow_policy.subscribe();
                let mut incoming_changed = flow_policy.subscribe();
                let outgoing = async {
                    loop {
                        let Some(outbound) = receiver.recv().await else {
                            break;
                        };
                        let mut remaining = outbound.bytes.len();
                        while remaining > 0 {
                            let length = remaining.min(flow_policy.chunk_size(
                                Domain::Internet,
                                Direction::FromAndroid,
                                TrafficClass::BestEffort,
                            ));
                            if flow_policy
                                .wait_session(
                                    Domain::Internet,
                                    Direction::FromAndroid,
                                    TrafficClass::BestEffort,
                                    length,
                                    &flow_generation,
                                    &mut outgoing_changed,
                                )
                                .await
                                .is_err()
                            {
                                return;
                            }
                            remaining -= length;
                        }
                        let start = Instant::now();
                        if upstream.send(&outbound.bytes).await.is_err() {
                            break;
                        }
                        flow_telemetry.record_wait(
                            Domain::Internet,
                            Direction::FromAndroid,
                            start.elapsed(),
                        );
                        flow_telemetry.record_bytes(
                            Domain::Internet,
                            Direction::FromAndroid,
                            outbound.bytes.len() as u64,
                            0,
                        );
                    }
                };
                let incoming = async {
                    let mut response = [0_u8; 65535];
                    loop {
                        let Ok(length) = upstream.recv(&mut response).await else {
                            break;
                        };
                        let mut remaining = length;
                        while remaining > 0 {
                            let chunk = remaining.min(flow_policy.chunk_size(
                                Domain::Internet,
                                Direction::ToAndroid,
                                TrafficClass::BestEffort,
                            ));
                            if flow_policy
                                .wait_session(
                                    Domain::Internet,
                                    Direction::ToAndroid,
                                    TrafficClass::BestEffort,
                                    chunk,
                                    &flow_generation,
                                    &mut incoming_changed,
                                )
                                .await
                                .is_err()
                            {
                                return;
                            }
                            remaining -= chunk;
                        }
                        if replies
                            .send_from_to(&response[..length], destination, source)
                            .await
                            .is_err()
                        {
                            break;
                        }
                        incoming_activity.store(udp_activity_millis(), Ordering::Relaxed);
                        flow_telemetry.record_bytes(
                            Domain::Internet,
                            Direction::ToAndroid,
                            length as u64,
                            0,
                        );
                    }
                };
                // Waiting for upload quota must not stop reading independent download replies.
                tokio::select! { _ = outgoing => {}, _ = incoming => {} }
            });
            flows.insert(key, (sender, last_activity, flow_memory));
        }
        if let Some((sender, last, flow_memory)) = flows.get_mut(&key) {
            last.store(udp_activity_millis(), Ordering::Relaxed);
            let permit = memory.clone().try_acquire_many_owned(count as u32);
            let flow_permit = flow_memory.clone().try_acquire_many_owned(count as u32);
            if let (Ok(permit), Ok(flow_permit)) = (permit, flow_permit) {
                let payload = UdpPayload {
                    bytes: packet[..count].to_vec(),
                    _memory: permit,
                    _flow_memory: flow_permit,
                };
                if sender.try_send(payload).is_err() {
                    telemetry.record_drop_at(
                        Domain::Internet,
                        Direction::FromAndroid,
                        "udp_queue_full",
                    );
                }
            } else {
                telemetry.record_drop_at(
                    Domain::Internet,
                    Direction::FromAndroid,
                    "udp_queue_full",
                );
            }
        }
    }
}

fn is_forwardable(destination: SocketAddr) -> bool {
    match destination.ip() {
        IpAddr::V4(address) => is_public_ipv4(address),
        IpAddr::V6(_) => false,
    }
}

fn is_forwardable_tcp(destination: SocketAddr) -> bool {
    is_speed_test_gateway(destination) || is_forwardable(destination)
}

fn is_speed_test_gateway(destination: SocketAddr) -> bool {
    matches!(
        destination,
        SocketAddr::V4(address)
            if *address.ip() == Ipv4Addr::new(10, 0, 2, 2)
                && address.port() == SPEED_TEST_PORT
    )
}

fn is_public_ipv4(address: Ipv4Addr) -> bool {
    let [a, b, c, d] = address.octets();

    // Only globally routable unicast IPv4 is eligible for Internet forwarding.
    // Local, shared, documentation, benchmark, multicast and reserved ranges
    // must never become a pivot into the PC or its LAN.
    if a == 0 || a == 10 || a == 127 || a >= 224 {
        return false;
    }
    if a == 100 && (64..=127).contains(&b) {
        return false;
    }
    if a == 169 && b == 254 {
        return false;
    }
    if a == 172 && (16..=31).contains(&b) {
        return false;
    }
    if a == 192 && b == 168 {
        return false;
    }
    if a == 192 && b == 0 && c == 0 && d != 9 && d != 10 {
        return false;
    }
    if a == 192 && b == 0 && c == 2 {
        return false;
    }
    if a == 192 && b == 88 && c == 99 {
        return false;
    }
    if a == 198 && (b == 18 || b == 19) {
        return false;
    }
    if a == 198 && b == 51 && c == 100 {
        return false;
    }
    if a == 203 && b == 0 && c == 113 {
        return false;
    }

    true
}

fn tcp_upstream(destination: SocketAddr) -> SocketAddr {
    match destination {
        SocketAddr::V4(address)
            if *address.ip() == Ipv4Addr::new(10, 0, 2, 2) && address.port() == SPEED_TEST_PORT =>
        {
            SocketAddr::from((Ipv4Addr::LOCALHOST, SPEED_TEST_PORT))
        }
        _ => destination,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::time::timeout;

    async fn authorized_connect(address: SocketAddr) -> TcpStream {
        let mut stream = TcpStream::connect(address).await.unwrap();
        write_frame(&mut stream, &format!("NOVORA-LINK|2|DATA|{TEST_TOKEN}"))
            .await
            .unwrap();
        let ack = timeout(Duration::from_secs(2), read_frame(&mut stream))
            .await
            .unwrap()
            .unwrap();
        assert_eq!(ack, DATA_AUTH_ACK);
        stream
    }

    #[test]
    fn udp_download_activity_prevents_idle_expiry() {
        assert!(udp_flow_is_idle(100, 60_100));
        assert!(!udp_flow_is_idle(50_000, 60_100));
        assert!(udp_flow_is_idle(50_000, 110_000));
    }

    #[test]
    fn only_speed_test_gateway_maps_to_pc_loopback() {
        assert_eq!(
            tcp_upstream(SocketAddr::from(([10, 0, 2, 2], SPEED_TEST_PORT))),
            SocketAddr::from(([127, 0, 0, 1], SPEED_TEST_PORT))
        );
        assert_eq!(
            tcp_upstream(SocketAddr::from(([10, 0, 2, 2], 80))),
            SocketAddr::from(([10, 0, 2, 2], 80))
        );
    }

    #[test]
    fn private_and_special_destinations_are_not_forwardable() {
        for address in [
            [0, 0, 0, 0],
            [10, 0, 0, 1],
            [10, 0, 2, 2],
            [100, 64, 0, 1],
            [127, 0, 0, 1],
            [169, 254, 169, 254],
            [172, 16, 0, 1],
            [172, 31, 255, 254],
            [192, 0, 0, 0],
            [192, 0, 0, 1],
            [192, 0, 0, 2],
            [192, 0, 0, 8],
            [192, 0, 0, 170],
            [192, 0, 0, 171],
            [192, 0, 0, 255],
            [192, 0, 2, 1],
            [192, 88, 99, 1],
            [192, 168, 1, 1],
            [198, 18, 0, 1],
            [198, 19, 255, 254],
            [198, 51, 100, 1],
            [203, 0, 113, 1],
            [224, 0, 0, 1],
            [239, 255, 255, 255],
            [240, 0, 0, 1],
            [255, 255, 255, 255],
        ] {
            assert!(
                !is_forwardable(SocketAddr::from((address, 443))),
                "{address:?} must not be forwarded"
            );
        }
    }

    #[test]
    fn public_destinations_are_forwardable() {
        for address in [
            [1, 1, 1, 1],
            [8, 8, 8, 8],
            [93, 184, 216, 34],
            [192, 0, 0, 9],
            [192, 0, 0, 10],
        ] {
            assert!(
                is_forwardable(SocketAddr::from((address, 443))),
                "{address:?} must be forwarded"
            );
        }
    }

    #[test]
    fn speed_test_gateway_is_only_allowed_for_tcp() {
        let speed_test = SocketAddr::from(([10, 0, 2, 2], SPEED_TEST_PORT));
        assert!(is_forwardable_tcp(speed_test));
        assert!(!is_forwardable(speed_test));
        assert!(!is_forwardable_tcp(SocketAddr::from(([10, 0, 2, 2], 80))));
        assert!(!is_forwardable_tcp(SocketAddr::from((
            [10, 0, 2, 3],
            SPEED_TEST_PORT
        ))));
    }

    #[tokio::test]
    async fn a_new_data_connection_replaces_a_stalled_one() {
        let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).await.unwrap();
        let address = listener.local_addr().unwrap();
        let server = tokio::spawn(run(listener));

        let mut first = authorized_connect(address).await;
        let mut id = [0_u8; 4];
        timeout(Duration::from_secs(2), first.read_exact(&mut id))
            .await
            .unwrap()
            .unwrap();
        assert_eq!(u32::from_be_bytes(id), 1);

        let mut second = authorized_connect(address).await;
        timeout(Duration::from_secs(2), second.read_exact(&mut id))
            .await
            .unwrap()
            .unwrap();
        assert_eq!(u32::from_be_bytes(id), 1);
        server.abort();
    }

    #[tokio::test]
    async fn invalid_token_does_not_replace_the_active_session() {
        let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).await.unwrap();
        let address = listener.local_addr().unwrap();
        let server = tokio::spawn(run(listener));

        let mut active = authorized_connect(address).await;
        let mut id = [0_u8; 4];
        timeout(Duration::from_secs(2), active.read_exact(&mut id))
            .await
            .unwrap()
            .unwrap();
        assert_eq!(u32::from_be_bytes(id), 1);

        let mut rejected = TcpStream::connect(address).await.unwrap();
        write_frame(
            &mut rejected,
            "NOVORA-LINK|2|DATA|ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
        )
        .await
        .unwrap();
        let mut response = [0_u8; 1];
        assert_eq!(
            timeout(Duration::from_secs(2), rejected.read(&mut response))
                .await
                .unwrap()
                .unwrap(),
            0
        );

        // The authenticated session must still be alive after the rejected attempt.
        assert!(
            timeout(Duration::from_millis(300), active.read(&mut response))
                .await
                .is_err()
        );
        server.abort();
    }

    #[tokio::test]
    async fn invalid_ipv4_closes_only_its_data_session() {
        let listener = TcpListener::bind((Ipv4Addr::LOCALHOST, 0)).await.unwrap();
        let address = listener.local_addr().unwrap();
        let server = tokio::spawn(run(listener));

        let mut first = authorized_connect(address).await;
        let mut id = [0_u8; 4];
        timeout(Duration::from_secs(2), first.read_exact(&mut id))
            .await
            .unwrap()
            .unwrap();
        let mut invalid = [0_u8; 20];
        invalid[0] = 0x45;
        invalid[3] = 10;
        first.write_all(&invalid).await.unwrap();
        let mut closed = [0_u8; 1];
        assert_eq!(
            timeout(Duration::from_secs(2), first.read(&mut closed))
                .await
                .unwrap()
                .unwrap(),
            0
        );

        let mut second = authorized_connect(address).await;
        timeout(Duration::from_secs(2), second.read_exact(&mut id))
            .await
            .unwrap()
            .unwrap();
        assert_eq!(u32::from_be_bytes(id), 1);
        server.abort();
    }
}
