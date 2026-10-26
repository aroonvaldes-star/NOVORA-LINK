use crate::policy::invalid;
use std::collections::{HashMap, VecDeque};
use std::io;
use std::time::Duration;

pub const QUANTUM: usize = 1500;
pub const MAX_BYTES: usize = 2 * 1024 * 1024;
pub const MAX_FLOW_BYTES: usize = 64 * 1024;
pub const MAX_FLOWS: usize = 256;

#[derive(Clone, Debug, Hash, Eq, PartialEq)]
pub struct FlowKey {
    source: [u8; 4],
    destination: [u8; 4],
    protocol: u8,
    source_port: Option<u16>,
    destination_port: Option<u16>,
    fragment_id: Option<u16>,
}

impl FlowKey {
    pub fn parse(packet: &[u8]) -> io::Result<Self> {
        if packet.len() < 20 || packet[0] >> 4 != 4 {
            return Err(invalid("invalid IPv4 header"));
        }
        let header = (packet[0] as usize & 15) * 4;
        let total = u16::from_be_bytes([packet[2], packet[3]]) as usize;
        if header < 20
            || header > packet.len()
            || total != packet.len()
            || total < header
            || total > 1500
        {
            return Err(invalid("invalid IPv4 packet length"));
        }
        let flags_offset = u16::from_be_bytes([packet[6], packet[7]]);
        if flags_offset & 0x8000 != 0 {
            return Err(invalid("reserved IPv4 fragmentation flag"));
        }
        let fragmented = flags_offset & 0x3fff != 0;
        let protocol = packet[9];
        let (source_port, destination_port) = if !fragmented && matches!(protocol, 6 | 17) {
            let minimum = if protocol == 6 { 20 } else { 8 };
            if total - header < minimum {
                return Err(invalid("truncated transport header"));
            }
            if protocol == 6 {
                let tcp_header = (packet[header + 12] as usize >> 4) * 4;
                if tcp_header < 20 || tcp_header > total - header {
                    return Err(invalid("invalid TCP header length"));
                }
            } else {
                let udp_length =
                    u16::from_be_bytes([packet[header + 4], packet[header + 5]]) as usize;
                if udp_length < 8 || udp_length != total - header {
                    return Err(invalid("invalid UDP length"));
                }
            }
            (
                Some(u16::from_be_bytes([packet[header], packet[header + 1]])),
                Some(u16::from_be_bytes([packet[header + 2], packet[header + 3]])),
            )
        } else {
            (None, None)
        };
        Ok(Self {
            source: packet[12..16].try_into().unwrap(),
            destination: packet[16..20].try_into().unwrap(),
            protocol,
            source_port,
            destination_port,
            fragment_id: fragmented.then(|| u16::from_be_bytes([packet[4], packet[5]])),
        })
    }
    #[cfg(test)]
    pub fn ports(&self) -> Option<(u16, u16)> {
        self.source_port.zip(self.destination_port)
    }
    pub fn is_udp(&self) -> bool {
        self.protocol == 17
    }
}

#[derive(Debug, Copy, Clone, Eq, PartialEq)]
pub enum QueueError {
    InvalidPacket,
    FlowLimit,
    FlowBytes,
    TotalBytes,
}
pub struct QueuedPacket {
    pub bytes: Vec<u8>,
    pub enqueued_at: Duration,
    sequence: u64,
    pub permit: Option<tokio::sync::OwnedSemaphorePermit>,
}
struct Flow {
    packets: VecDeque<QueuedPacket>,
    bytes: usize,
    deficit: usize,
}

/// DRR on complete IPv4 packets. Unknown TCP/UDP have the same BestEffort service.
pub struct PacketQueue {
    flows: HashMap<FlowKey, Flow>,
    active: VecDeque<FlowKey>,
    bytes: usize,
    sequence: u64,
}

impl PacketQueue {
    pub fn new() -> Self {
        Self {
            flows: HashMap::new(),
            active: VecDeque::new(),
            bytes: 0,
            sequence: 0,
        }
    }
    pub fn enqueue(&mut self, packet: Vec<u8>, now: Duration) -> Result<(), QueueError> {
        let key = FlowKey::parse(&packet).map_err(|_| QueueError::InvalidPacket)?;
        self.enqueue_key(key, packet, now)
    }
    pub fn enqueue_key(
        &mut self,
        key: FlowKey,
        packet: Vec<u8>,
        now: Duration,
    ) -> Result<(), QueueError> {
        self.can_enqueue(&key, packet.len())?;
        let flow = self.flows.entry(key.clone()).or_insert_with(|| {
            self.active.push_back(key);
            Flow {
                packets: VecDeque::new(),
                bytes: 0,
                deficit: 0,
            }
        });
        self.bytes += packet.len();
        flow.bytes += packet.len();
        self.sequence += 1;
        flow.packets.push_back(QueuedPacket {
            bytes: packet,
            enqueued_at: now,
            sequence: self.sequence,
            permit: None,
        });
        Ok(())
    }
    pub fn can_enqueue(&self, key: &FlowKey, length: usize) -> Result<(), QueueError> {
        if self.bytes + length > MAX_BYTES {
            return Err(QueueError::TotalBytes);
        }
        if let Some(flow) = self.flows.get(&key) {
            if flow.bytes + length > MAX_FLOW_BYTES {
                return Err(QueueError::FlowBytes);
            }
        } else if self.flows.len() == MAX_FLOWS {
            return Err(QueueError::FlowLimit);
        }
        Ok(())
    }
    pub fn dequeue(&mut self, _now: Duration) -> Option<QueuedPacket> {
        while let Some(key) = self.active.pop_front() {
            let flow = self.flows.get_mut(&key).unwrap();
            let packet = flow.packets.front().unwrap();
            if flow.deficit < packet.bytes.len() {
                flow.deficit = flow.deficit.saturating_add(QUANTUM);
            }
            if packet.bytes.len() > flow.deficit {
                self.active.push_back(key);
                continue;
            }
            let packet = flow.packets.pop_front().unwrap();
            flow.deficit -= packet.bytes.len();
            flow.bytes -= packet.bytes.len();
            self.bytes -= packet.bytes.len();
            if flow.packets.is_empty() {
                self.flows.remove(&key);
            } else if flow.packets.front().unwrap().bytes.len() <= flow.deficit {
                self.active.push_front(key);
            } else {
                self.active.push_back(key);
            }
            return Some(packet);
        }
        None
    }
    pub fn dequeue_fifo(&mut self) -> Option<QueuedPacket> {
        let key = self
            .flows
            .iter()
            .min_by_key(|(_, flow)| flow.packets.front().unwrap().sequence)?
            .0
            .clone();
        let flow = self.flows.get_mut(&key).unwrap();
        let packet = flow.packets.pop_front().unwrap();
        flow.bytes -= packet.bytes.len();
        self.bytes -= packet.bytes.len();
        if flow.packets.is_empty() {
            self.flows.remove(&key);
            self.active.retain(|candidate| *candidate != key);
        }
        Some(packet)
    }
    pub fn attach_last_permit(&mut self, key: &FlowKey, permit: tokio::sync::OwnedSemaphorePermit) {
        self.flows
            .get_mut(key)
            .unwrap()
            .packets
            .back_mut()
            .unwrap()
            .permit = Some(permit);
    }
    pub fn age_p95(&self, now: Duration) -> Option<Duration> {
        let mut ages: Vec<Duration> = self
            .flows
            .values()
            .flat_map(|flow| &flow.packets)
            .map(|packet| now.saturating_sub(packet.enqueued_at))
            .collect();
        if ages.is_empty() {
            return None;
        }
        ages.sort_unstable();
        Some(ages[(ages.len() * 95).div_ceil(100).saturating_sub(1)])
    }
    #[cfg(test)]
    pub fn is_empty(&self) -> bool {
        self.bytes == 0
    }
    pub fn queued_bytes(&self) -> usize {
        self.bytes
    }
}
