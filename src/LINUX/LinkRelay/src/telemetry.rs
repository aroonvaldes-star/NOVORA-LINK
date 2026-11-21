use crate::policy::{Config, Direction, Domain};
use serde::Serialize;
use std::collections::{BTreeMap, HashMap};
use std::sync::Mutex;
use std::time::{Duration, Instant};

const SAMPLE_LIMIT: usize = 128;
#[derive(Default)]
struct Window {
    useful: u64,
    protocol: u64,
    queue_bytes: u64,
    dropped: u64,
    queue_ages: Vec<f64>,
    waits: Vec<f64>,
    current_queue_p95: Option<f64>,
}
#[derive(Default)]
struct Windows {
    windows: [Window; 4],
    drops: BTreeMap<String, u64>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Sample {
    pub domain: Domain,
    pub direction: Direction,
    pub source: &'static str,
    pub useful_bytes: u64,
    pub protocol_bytes: u64,
    pub queue_bytes: u64,
    pub queue_age_p95_ms: Option<f64>,
    pub write_wait_p95_ms: Option<f64>,
    pub dropped: u64,
    pub valid: bool,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    v: u32,
    revision: u64,
    generation: u64,
    link_key: String,
    mode: crate::policy::Mode,
    sequence: u64,
    window_seconds: f64,
    pub samples: Vec<Sample>,
    pub drops: BTreeMap<String, u64>,
}

pub struct Telemetry {
    windows: Mutex<Windows>,
    pending: Mutex<Pending>,
}

#[derive(Default)]
struct Pending {
    next: u64,
    buffers: HashMap<(usize, u64), Buffer>,
}
struct Buffer {
    bytes: usize,
    ready: Instant,
    writing: Option<Instant>,
}
pub struct PendingBuffer<'a> {
    telemetry: &'a Telemetry,
    domain: Domain,
    direction: Direction,
    id: u64,
}
impl PendingBuffer<'_> {
    pub fn writing(&self) {
        if let Some(buffer) = self
            .telemetry
            .pending
            .lock()
            .unwrap()
            .buffers
            .get_mut(&(index(self.domain, self.direction), self.id))
        {
            buffer.writing = Some(Instant::now());
        }
    }
}
impl Drop for PendingBuffer<'_> {
    fn drop(&mut self) {
        let buffer = self
            .telemetry
            .pending
            .lock()
            .unwrap()
            .buffers
            .remove(&(index(self.domain, self.direction), self.id));
        if let Some(buffer) = buffer {
            append(
                &mut self.telemetry.windows.lock().unwrap().windows
                    [index(self.domain, self.direction)]
                .queue_ages,
                buffer.ready.elapsed(),
            );
        }
    }
}

fn index(domain: Domain, direction: Direction) -> usize {
    usize::from(domain == Domain::DeviceLink) * 2 + usize::from(direction == Direction::ToAndroid)
}
fn append(samples: &mut Vec<f64>, elapsed: Duration) {
    if samples.len() == SAMPLE_LIMIT {
        samples.remove(0);
    }
    samples.push(elapsed.as_secs_f64() * 1000.0);
}
fn percentile(samples: &mut [f64]) -> Option<f64> {
    if samples.is_empty() {
        return None;
    }
    samples.sort_by(f64::total_cmp);
    Some(samples[(samples.len() * 95).div_ceil(100).saturating_sub(1)])
}

impl Telemetry {
    pub fn new() -> Self {
        Self {
            windows: Mutex::new(Windows::default()),
            pending: Mutex::new(Pending::default()),
        }
    }
    pub fn buffer(&self, domain: Domain, direction: Direction, bytes: usize) -> PendingBuffer<'_> {
        let mut pending = self.pending.lock().unwrap();
        pending.next = pending.next.wrapping_add(1);
        let id = pending.next;
        pending.buffers.insert(
            (index(domain, direction), id),
            Buffer {
                bytes,
                ready: Instant::now(),
                writing: None,
            },
        );
        PendingBuffer {
            telemetry: self,
            domain,
            direction,
            id,
        }
    }
    pub fn record_bytes(&self, domain: Domain, direction: Direction, useful: u64, protocol: u64) {
        let mut data = self.windows.lock().unwrap();
        let window = &mut data.windows[index(domain, direction)];
        window.useful = window.useful.saturating_add(useful);
        window.protocol = window.protocol.saturating_add(protocol);
    }
    pub fn record_wait(&self, domain: Domain, direction: Direction, elapsed: Duration) {
        append(
            &mut self.windows.lock().unwrap().windows[index(domain, direction)].waits,
            elapsed,
        );
    }
    pub fn record_queue(&self, domain: Domain, direction: Direction, bytes: usize, age: Duration) {
        let mut data = self.windows.lock().unwrap();
        let window = &mut data.windows[index(domain, direction)];
        window.queue_bytes = bytes as u64;
        append(&mut window.queue_ages, age);
    }
    pub fn set_queue(
        &self,
        domain: Domain,
        direction: Direction,
        bytes: usize,
        p95: Option<Duration>,
    ) {
        let mut data = self.windows.lock().unwrap();
        let window = &mut data.windows[index(domain, direction)];
        window.queue_bytes = bytes as u64;
        window.current_queue_p95 = p95.map(|age| age.as_secs_f64() * 1000.0);
    }
    #[cfg(test)]
    pub fn record_drop(&self, reason: &'static str) {
        self.record_drop_at(Domain::DeviceLink, Direction::FromAndroid, reason);
    }
    pub fn record_drop_at(&self, domain: Domain, direction: Direction, reason: &'static str) {
        let mut data = self.windows.lock().unwrap();
        *data.drops.entry(reason.to_string()).or_default() += 1;
        data.windows[index(domain, direction)].dropped += 1;
    }
    pub fn snapshot(&self, config: &Config, sequence: u64, elapsed: Duration) -> Snapshot {
        let mut windows = self.windows.lock().unwrap();
        let old = std::mem::take(&mut *windows);
        // Queue occupancy is a gauge, not a counter: carry it across reporting windows.
        for (new, old) in windows.windows.iter_mut().zip(&old.windows) {
            new.queue_bytes = old.queue_bytes;
        }
        let mut samples = Vec::with_capacity(4);
        let pending = self.pending.lock().unwrap();
        for (i, mut window) in old.windows.into_iter().enumerate() {
            let mut pending_ages = Vec::new();
            let mut pending_bytes = 0u64;
            for ((scope, _), buffer) in &pending.buffers {
                if *scope != i {
                    continue;
                }
                pending_bytes += buffer.bytes as u64;
                pending_ages.push(buffer.ready.elapsed().as_secs_f64() * 1000.0);
                if let Some(start) = buffer.writing {
                    window.waits.push(start.elapsed().as_secs_f64() * 1000.0);
                }
            }
            samples.push(Sample {
                domain: if i < 2 {
                    Domain::Internet
                } else {
                    Domain::DeviceLink
                },
                direction: if i % 2 == 0 {
                    Direction::FromAndroid
                } else {
                    Direction::ToAndroid
                },
                source: "Pipeline",
                useful_bytes: window.useful,
                protocol_bytes: window.protocol,
                queue_bytes: window.queue_bytes + pending_bytes,
                queue_age_p95_ms: percentile(&mut pending_ages).or(window
                    .current_queue_p95
                    .or_else(|| percentile(&mut window.queue_ages))),
                write_wait_p95_ms: percentile(&mut window.waits),
                dropped: window.dropped,
                valid: elapsed.as_secs_f64() > 0.0,
            });
        }
        Snapshot {
            v: 1,
            revision: config.revision,
            generation: config.generation,
            link_key: config.link_key.clone(),
            mode: config.mode,
            sequence,
            window_seconds: elapsed.as_secs_f64(),
            samples,
            drops: old.drops,
        }
    }
}
