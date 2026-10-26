use crate::flow_scheduler::{FlowKey, PacketQueue, QueueError};
use crate::policy::{Config, Direction, Domain, PolicyState, TrafficClass};
use crate::rate_limit::RateLimiter;
use crate::telemetry::Telemetry;
use std::sync::Arc;
use std::time::Duration;

async fn authorized_client(address: std::net::SocketAddr) -> tokio::net::TcpStream {
    use tokio::io::AsyncReadExt;
    let mut client = tokio::net::TcpStream::connect(address).await.unwrap();
    crate::write_frame(
        &mut client,
        &format!("NOVORA-LINK|2|DATA|{}", crate::TEST_TOKEN),
    )
    .await
    .unwrap();
    let mut length = [0_u8; 4];
    client.read_exact(&mut length).await.unwrap();
    let mut ack = vec![0_u8; u32::from_be_bytes(length) as usize];
    client.read_exact(&mut ack).await.unwrap();
    assert_eq!(String::from_utf8(ack).unwrap(), crate::DATA_AUTH_ACK);
    client
}

fn config(revision: u64, generation: u64) -> Config {
    Config::parse(format!(r#"{{"v":1,"revision":{revision},"generation":{generation},"linkKey":"usb:test","mode":"Manual","wanUploadBps":1000,"wanDownloadBps":2000,"deviceFromAndroidBps":3000,"deviceToAndroidBps":4000,"grants":[]}}"#).as_bytes()).unwrap()
}

#[test]
fn exact_rate_no_burst_after_idle() {
    let mut limiter = RateLimiter::new(1000, 1000, Duration::ZERO);
    assert_eq!(
        limiter.reserve(2000, Duration::ZERO),
        Duration::from_secs(1)
    );
    assert_eq!(
        limiter.reserve(2000, Duration::from_secs(60)),
        Duration::from_secs(1)
    );
}

#[test]
fn quota_update_does_not_create_fresh_burst() {
    let mut limiter = RateLimiter::new(1000, 1000, Duration::ZERO);
    assert_eq!(limiter.reserve(1000, Duration::ZERO), Duration::ZERO);
    limiter.update(500, 1000, Duration::ZERO);
    assert_eq!(limiter.reserve(500, Duration::ZERO), Duration::from_secs(1));
}

#[test]
fn configuration_is_strict_bounded_and_domain_aware() {
    assert!(Config::parse(&vec![b' '; 4097]).is_err());
    let original = config(1, 7);
    let json = serde_json::to_string(&original).unwrap();
    assert!(Config::parse(json.replace("1000", "-1").as_bytes()).is_err());
    assert!(Config::parse(json.replace("1000", "1250000001").as_bytes()).is_err());
    assert!(Config::parse(json.replace("\"Manual\"", "\"Automatic\"").as_bytes()).is_err());
    assert!(Config::parse(json.replace("\"v\":1", "\"v\":2").as_bytes()).is_err());
    let over = json.replace("\"grants\":[]", r#""grants":[{"domain":"Internet","direction":"FromAndroid","trafficClass":"Bulk","bytesPerSecond":1001,"burstBytes":1500}]"#);
    assert!(Config::parse(over.as_bytes()).is_err());
    let duplicate = json.replace("\"grants\":[]", r#""grants":[{"domain":"Internet","direction":"FromAndroid","trafficClass":"Bulk","bytesPerSecond":1,"burstBytes":1500},{"domain":"Internet","direction":"FromAndroid","trafficClass":"Bulk","bytesPerSecond":1,"burstBytes":1500}]"#);
    assert!(Config::parse(duplicate.as_bytes()).is_err());
    assert!(Config::parse(json.replace("\"mode\"", "\"unknown\"").as_bytes()).is_err());
}

#[tokio::test]
async fn revision_ack_is_atomic_and_generation_aware() {
    let state = PolicyState::new();
    assert!(state.apply(config(2, 7)).accepted);
    assert!(state.apply(config(2, 7)).accepted);
    assert!(!state.apply(config(1, 7)).accepted);
    let mut changed = config(2, 7);
    changed.wan_upload_bps = Some(9);
    assert!(!state.apply(changed).accepted);
    assert!(!state.apply(config(9, 6)).accepted);
    assert!(state.apply(config(1, 8)).accepted);
    assert_eq!(state.snapshot().generation, 8);
    assert_eq!(state.snapshot().wan_upload_bps, Some(1000));
}

#[tokio::test]
async fn shared_tcp_udp_budget_and_separate_wan_device_grants() {
    let state = PolicyState::new();
    assert!(state.apply(config(1, 7)).accepted);
    let now = Duration::ZERO;
    assert_eq!(
        state.reserve_at(
            Domain::Internet,
            Direction::FromAndroid,
            TrafficClass::BestEffort,
            1500,
            now
        ),
        Duration::ZERO
    );
    assert_eq!(
        state.reserve_at(
            Domain::Internet,
            Direction::FromAndroid,
            TrafficClass::BestEffort,
            1000,
            now
        ),
        Duration::from_secs(1)
    );
    assert_eq!(
        state.reserve_at(
            Domain::DeviceLink,
            Direction::FromAndroid,
            TrafficClass::BestEffort,
            1500,
            now
        ),
        Duration::ZERO
    );
    assert_eq!(
        state.reserve_at(
            Domain::Internet,
            Direction::ToAndroid,
            TrafficClass::BestEffort,
            1500,
            now
        ),
        Duration::ZERO
    );
}

#[tokio::test]
async fn cancel_pending_wait_and_generation_replacement() {
    let state = Arc::new(PolicyState::new());
    state.apply(config(1, 7));
    state.reserve_at(
        Domain::Internet,
        Direction::FromAndroid,
        TrafficClass::BestEffort,
        100000,
        Duration::ZERO,
    );
    let mut cancel = state.subscribe();
    let pending = state.wait(
        Domain::Internet,
        Direction::FromAndroid,
        TrafficClass::BestEffort,
        1500,
        7,
        &mut cancel,
    );
    let change = async {
        tokio::task::yield_now().await;
        state.apply(config(1, 8));
    };
    let (result, _) = tokio::join!(pending, change);
    assert_eq!(result.unwrap_err().kind(), std::io::ErrorKind::Interrupted);
}

fn packet(source_port: u16, sequence: u8) -> Vec<u8> {
    let mut packet = vec![0; 1000];
    packet[0] = 0x45;
    packet[2..4].copy_from_slice(&1000u16.to_be_bytes());
    packet[9] = 17;
    packet[12..16].copy_from_slice(&[10, 0, 0, 1]);
    packet[16..20].copy_from_slice(&[1, 1, 1, 1]);
    packet[20..22].copy_from_slice(&source_port.to_be_bytes());
    packet[22..24].copy_from_slice(&443u16.to_be_bytes());
    packet[24..26].copy_from_slice(&980u16.to_be_bytes());
    packet[28] = sequence;
    packet
}

#[test]
fn invalid_fragment_not_classified() {
    let mut p = packet(9, 0);
    p[7] = 1;
    assert!(FlowKey::parse(&p).unwrap().ports().is_none());
    p[7] = 0;
    p[0] = 0x46;
    p.truncate(22);
    assert!(FlowKey::parse(&p).is_err());
}

#[test]
fn fair_flows_preserve_fifo_and_no_starvation() {
    let mut queue = PacketQueue::new();
    for sequence in 0..20 {
        for flow in 1..=8 {
            queue
                .enqueue(packet(flow, sequence), Duration::ZERO)
                .unwrap();
        }
    }
    queue.enqueue(packet(99, 0), Duration::ZERO).unwrap();
    let mut counts = [0u64; 100];
    for _ in 0..161 {
        let p = queue.dequeue(Duration::from_secs(1)).unwrap();
        let flow = u16::from_be_bytes([p.bytes[20], p.bytes[21]]) as usize;
        assert_eq!(p.bytes[28] as u64, counts[flow]);
        counts[flow] += 1;
    }
    assert_eq!(counts[99], 1);
    assert_eq!(&counts[1..9], &[20; 8]);
    assert!(queue.is_empty());
}

#[test]
fn bounded_memory_many_flows() {
    let mut queue = PacketQueue::new();
    for flow in 1..=256 {
        queue.enqueue(packet(flow, 0), Duration::ZERO).unwrap();
    }
    assert_eq!(
        queue.enqueue(packet(257, 0), Duration::ZERO).unwrap_err(),
        QueueError::FlowLimit
    );
    let mut single = PacketQueue::new();
    for _ in 0..65 {
        single.enqueue(packet(1, 0), Duration::ZERO).unwrap();
    }
    assert_eq!(
        single.enqueue(packet(1, 0), Duration::ZERO).unwrap_err(),
        QueueError::FlowBytes
    );
    assert!(queue.queued_bytes() <= 2 * 1024 * 1024);
}

#[test]
fn qos_windows_reset_without_altering_legacy_totals() {
    let metrics = Telemetry::new();
    metrics.record_bytes(Domain::Internet, Direction::FromAndroid, 100, 20);
    metrics.record_wait(
        Domain::DeviceLink,
        Direction::ToAndroid,
        Duration::from_millis(8),
    );
    metrics.record_drop("udp_queue_full");
    let first = metrics.snapshot(&config(1, 7), 1, Duration::from_secs(1));
    assert!(serde_json::to_vec(&first).unwrap().len() <= 4096);
    assert_eq!(first.samples[0].useful_bytes, 100);
    assert_eq!(first.drops["udp_queue_full"], 1);
    let second = metrics.snapshot(&config(1, 7), 2, Duration::from_secs(1));
    assert_eq!(second.samples[0].useful_bytes, 0);
    assert!(second.drops.is_empty());
}

#[test]
fn json_duplicate_properties_are_rejected() {
    let json = serde_json::to_string(&config(1, 7)).unwrap();
    assert!(Config::parse(json.replacen("\"v\":1", "\"v\":1,\"v\":1", 1).as_bytes()).is_err());
    let duplicate = json.replace("\"grants\":[]", r#""grants":[{"domain":"Internet","direction":"FromAndroid","trafficClass":"Bulk","bytesPerSecond":0,"bytesPerSecond":1,"burstBytes":1}]"#);
    assert!(Config::parse(duplicate.as_bytes()).is_err());
}

#[tokio::test]
async fn bounded_stdin_drains_oversize_then_reads_next_config() {
    use tokio::io::{AsyncWriteExt, BufReader};
    let (mut writer, reader) = tokio::io::duplex(16384);
    writer.write_all(&vec![b'x'; 4097]).await.unwrap();
    writer.write_all(b"\nLE_CONFIG {}\n").await.unwrap();
    drop(writer);
    let mut reader = BufReader::new(reader);
    assert!(crate::policy::read_line_bounded(&mut reader).await.is_err());
    assert_eq!(
        crate::policy::read_line_bounded(&mut reader)
            .await
            .unwrap()
            .unwrap(),
        b"LE_CONFIG {}"
    );
    assert!(crate::policy::read_line_bounded(&mut reader)
        .await
        .unwrap()
        .is_none());
}

#[tokio::test]
async fn applied_generation_replaces_active_data_session() {
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpListener;
    let listener = TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, 0))
        .await
        .unwrap();
    let address = listener.local_addr().unwrap();
    let policy = Arc::new(PolicyState::new());
    policy.apply(config(1, 7));
    let server = tokio::spawn(crate::run_with_policy(
        listener,
        policy.clone(),
        Arc::new(crate::TEST_TOKEN.to_string()),
    ));
    let mut client = authorized_client(address).await;
    let mut id = [0u8; 4];
    client.read_exact(&mut id).await.unwrap();
    assert_eq!(id, [0, 0, 0, 1]);
    policy.apply(config(1, 8));
    let mut one = [0u8; 1];
    assert_eq!(
        tokio::time::timeout(Duration::from_secs(2), client.read(&mut one))
            .await
            .unwrap()
            .unwrap(),
        0
    );
    let mut next = authorized_client(address).await;
    next.read_exact(&mut id).await.unwrap();
    assert_eq!(id, [0, 0, 0, 1]);
    next.shutdown().await.unwrap();
    server.abort();
}

#[test]
fn drr_is_fair_in_bytes_for_different_packet_sizes() {
    let mut queue = PacketQueue::new();
    for _ in 0..40 {
        let mut small = packet(1, 0);
        small.truncate(500);
        small[2..4].copy_from_slice(&500u16.to_be_bytes());
        small[24..26].copy_from_slice(&480u16.to_be_bytes());
        queue.enqueue(small, Duration::ZERO).unwrap();
        let mut large = packet(2, 0);
        large.resize(1500, 0);
        large[2..4].copy_from_slice(&1500u16.to_be_bytes());
        large[24..26].copy_from_slice(&1480u16.to_be_bytes());
        queue.enqueue(large, Duration::ZERO).unwrap();
    }
    let mut served = [0u64; 2];
    for _ in 0..1000 {
        let p = queue.dequeue(Duration::ZERO).unwrap();
        let index = u16::from_be_bytes([p.bytes[20], p.bytes[21]]) as usize - 1;
        served[index] += p.bytes.len() as u64;
        queue.enqueue(p.bytes, Duration::ZERO).unwrap();
    }
    let ratio = served[0] as f64 / served[1] as f64;
    assert!((0.8..=1.2).contains(&ratio), "served bytes: {served:?}");
}

#[tokio::test]
async fn copy_preserves_short_reads_and_half_close() {
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    let (mut sender, mut client) = tokio::io::duplex(37);
    let (mut upstream, mut receiver) = tokio::io::duplex(41);
    let state = Arc::new(PolicyState::new());
    let telemetry = Arc::new(Telemetry::new());
    let generation = state.bind_session();
    let relay = tokio::spawn(async move {
        crate::proxy_copy::copy_proxy(
            &mut client,
            &mut upstream,
            state,
            telemetry,
            generation,
            true,
        )
        .await
    });
    let payload = vec![0xa5; 10001];
    let send = async {
        sender.write_all(&payload).await.unwrap();
        sender.shutdown().await.unwrap();
    };
    let receive = async {
        let mut result = Vec::new();
        receiver.read_to_end(&mut result).await.unwrap();
        assert_eq!(result, payload);
        receiver.shutdown().await.unwrap();
    };
    tokio::join!(send, receive);
    relay.await.unwrap().unwrap();
}

#[tokio::test]
async fn zero_bulk_grant_waits_for_policy_without_sending() {
    let state = PolicyState::new();
    let mut paused = config(1, 7);
    paused.grants.push(crate::policy::Grant {
        domain: Domain::Internet,
        direction: Direction::FromAndroid,
        traffic_class: TrafficClass::Bulk,
        bytes_per_second: 0,
        burst_bytes: 1,
    });
    assert!(state.apply(paused.clone()).accepted);
    let mut changed = state.subscribe();
    assert!(tokio::time::timeout(
        Duration::from_millis(10),
        state.wait(
            Domain::Internet,
            Direction::FromAndroid,
            TrafficClass::Bulk,
            1,
            7,
            &mut changed
        )
    )
    .await
    .is_err());
    paused.revision = 2;
    paused.grants[0].bytes_per_second = 1000;
    state.apply(paused);
    tokio::time::timeout(
        Duration::from_secs(1),
        state.wait(
            Domain::Internet,
            Direction::FromAndroid,
            TrafficClass::Bulk,
            1,
            7,
            &mut changed,
        ),
    )
    .await
    .unwrap()
    .unwrap();
}

#[test]
fn short_read_refund_preserves_only_actual_byte_charge() {
    let mut limiter = RateLimiter::new(1000, 1000, Duration::ZERO);
    limiter.reserve(1000, Duration::ZERO);
    limiter.refund(999);
    assert_eq!(limiter.reserve(999, Duration::ZERO), Duration::ZERO);
    assert_eq!(limiter.reserve(1, Duration::ZERO), Duration::from_millis(1));
}

#[tokio::test]
async fn sparse_large_reservation_is_not_starved_by_small_requests() {
    let state = Arc::new(PolicyState::new());
    let mut policy = config(1, 7);
    policy.wan_upload_bps = Some(100_000);
    state.apply(policy);
    state.reserve_at(
        Domain::Internet,
        Direction::FromAndroid,
        TrafficClass::BestEffort,
        1500,
        state.elapsed(),
    );
    let large_state = state.clone();
    let mut large = tokio::spawn(async move {
        let mut changed = large_state.subscribe();
        large_state
            .wait(
                Domain::Internet,
                Direction::FromAndroid,
                TrafficClass::BestEffort,
                1500,
                7,
                &mut changed,
            )
            .await
    });
    tokio::task::yield_now().await;
    let small_state = state.clone();
    let small = tokio::spawn(async move {
        let mut changed = small_state.subscribe();
        let start = std::time::Instant::now();
        while start.elapsed() < Duration::from_millis(200) {
            small_state
                .wait(
                    Domain::Internet,
                    Direction::FromAndroid,
                    TrafficClass::BestEffort,
                    1,
                    7,
                    &mut changed,
                )
                .await
                .unwrap();
            tokio::task::yield_now().await;
        }
    });
    let result = tokio::time::timeout(Duration::from_millis(80), &mut large).await;
    small.abort();
    large.abort();
    assert!(
        result.is_ok(),
        "large pending reservation starved under small-request load"
    );
}

#[tokio::test]
async fn slow_upstreams_cannot_accumulate_an_unbounded_actual_download_burst() {
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    let state = Arc::new(PolicyState::new());
    let mut policy = config(1, 7);
    policy.wan_download_bps = Some(100_000);
    state.apply(policy);
    let barrier = Arc::new(tokio::sync::Barrier::new(4));
    let mut receivers = Vec::new();
    let mut relays = Vec::new();
    for _ in 0..3 {
        let (mut peer, mut client) = tokio::io::duplex(8192);
        let (mut upstream, mut source) = tokio::io::duplex(8192);
        let state = state.clone();
        let generation = state.bind_session();
        relays.push(tokio::spawn(async move {
            crate::proxy_copy::copy_proxy(
                &mut client,
                &mut upstream,
                state,
                Arc::new(Telemetry::new()),
                generation,
                true,
            )
            .await
        }));
        let barrier = barrier.clone();
        tokio::spawn(async move {
            barrier.wait().await;
            source.write_all(&[0x5a; 1500]).await.unwrap();
            source.shutdown().await.unwrap();
        });
        receivers.push(tokio::spawn(async move {
            let mut bytes = [0u8; 1500];
            peer.read_exact(&mut bytes).await.unwrap();
            assert_eq!(bytes, [0x5a; 1500]);
            peer.shutdown().await.unwrap();
        }));
    }
    tokio::time::sleep(Duration::from_millis(60)).await;
    let start = std::time::Instant::now();
    barrier.wait().await;
    for receiver in receivers {
        receiver.await.unwrap();
    }
    let elapsed = start.elapsed();
    for relay in relays {
        relay.abort();
    }
    assert!(elapsed >= Duration::from_millis(20), "actual 4500-byte download burst escaped the shared 100000B/s, 1500B burst limit: {elapsed:?}");
}

#[tokio::test]
async fn first_policy_adopts_existing_data_then_enforces_bound_generation() {
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpListener;
    let listener = TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, 0))
        .await
        .unwrap();
    let address = listener.local_addr().unwrap();
    let policy = Arc::new(PolicyState::new());
    let server = tokio::spawn(crate::run_with_policy(
        listener,
        policy.clone(),
        Arc::new(crate::TEST_TOKEN.to_string()),
    ));
    let mut client = authorized_client(address).await;
    let mut id = [0u8; 4];
    client.read_exact(&mut id).await.unwrap();
    assert_eq!(id, [0, 0, 0, 1]);
    let mut ipv4 = [0u8; 20];
    ipv4[0] = 0x45;
    ipv4[3] = 20;
    ipv4[8] = 64;
    ipv4[9] = 253;
    ipv4[12..16].copy_from_slice(&[10, 0, 2, 2]);
    ipv4[16..20].copy_from_slice(&[1, 1, 1, 1]);
    let mut sum: u32 = ipv4
        .chunks_exact(2)
        .map(|word| u16::from_be_bytes([word[0], word[1]]) as u32)
        .sum();
    while sum >> 16 != 0 {
        sum = (sum & 65535) + (sum >> 16);
    }
    ipv4[10..12].copy_from_slice(&(!(sum as u16)).to_be_bytes());
    client.write_all(&ipv4).await.unwrap();
    tokio::time::sleep(Duration::from_millis(5)).await;
    assert!(policy.apply(config(1, 7)).accepted);
    client.write_all(&ipv4).await.unwrap();
    let mut byte = [0u8; 1];
    assert!(
        tokio::time::timeout(Duration::from_millis(30), client.read(&mut byte))
            .await
            .is_err(),
        "first configured generation closed existing DATA"
    );
    assert!(policy.apply(config(2, 7)).accepted);
    client.write_all(&ipv4).await.unwrap();
    assert!(
        tokio::time::timeout(Duration::from_millis(30), client.read(&mut byte))
            .await
            .is_err(),
        "same-generation revision closed DATA"
    );
    assert!(policy.apply(config(1, 8)).accepted);
    assert_eq!(
        tokio::time::timeout(Duration::from_secs(2), client.read(&mut byte))
            .await
            .unwrap()
            .unwrap(),
        0
    );
    server.abort();
}

#[tokio::test]
async fn wan_buffer_age_and_inflight_write_are_visible_without_device_attribution() {
    let metrics = Telemetry::new();
    let buffer = metrics.buffer(Domain::Internet, Direction::FromAndroid, 1200);
    buffer.writing();
    tokio::time::sleep(Duration::from_millis(10)).await;
    let snapshot = metrics.snapshot(&config(1, 7), 1, Duration::from_secs(1));
    assert_eq!(snapshot.samples[0].queue_bytes, 1200);
    assert!(snapshot.samples[0].queue_age_p95_ms.unwrap() >= 5.0);
    assert!(snapshot.samples[0].write_wait_p95_ms.unwrap() >= 5.0);
    assert_eq!(snapshot.samples[2].queue_bytes, 0);
    assert!(snapshot.samples[2].queue_age_p95_ms.is_none());
    drop(buffer);
    let snapshot = metrics.snapshot(&config(1, 7), 2, Duration::from_secs(1));
    assert_eq!(snapshot.samples[0].queue_bytes, 0);
}
