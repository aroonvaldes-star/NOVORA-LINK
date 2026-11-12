use crate::rate_limit::RateLimiter;
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet, VecDeque};
use std::io;
use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc, Mutex, Weak,
};
use std::time::{Duration, Instant};
use tokio::io::{AsyncBufRead, AsyncBufReadExt};
use tokio::sync::{watch, Notify};

pub const MAX_LINE: usize = 4096;
/// Read size for unshaped TCP copies; also the buffer size that backs them.
pub const UNSHAPED_CHUNK: usize = 64 * 1024;
pub const MAX_BPS: u64 = 1_250_000_000;

#[derive(Debug, Copy, Clone, Hash, Eq, PartialEq, Serialize, Deserialize)]
pub enum Domain {
    Internet,
    DeviceLink,
}
#[derive(Debug, Copy, Clone, Hash, Eq, PartialEq, Serialize, Deserialize)]
pub enum Direction {
    FromAndroid,
    ToAndroid,
}
#[derive(Debug, Copy, Clone, Hash, Eq, PartialEq, Serialize, Deserialize)]
pub enum TrafficClass {
    Interactive,
    Audio,
    Video,
    BestEffort,
    Bulk,
}
#[derive(Debug, Copy, Clone, Eq, PartialEq, Serialize, Deserialize)]
pub enum Mode {
    Disabled,
    Observe,
    Manual,
    Adaptive,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Grant {
    pub domain: Domain,
    pub direction: Direction,
    pub traffic_class: TrafficClass,
    pub bytes_per_second: u64,
    pub burst_bytes: u64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Config {
    pub v: u32,
    pub revision: u64,
    pub generation: u64,
    pub link_key: String,
    pub mode: Mode,
    pub wan_upload_bps: Option<u64>,
    pub wan_download_bps: Option<u64>,
    pub device_from_android_bps: Option<u64>,
    pub device_to_android_bps: Option<u64>,
    pub grants: Vec<Grant>,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            v: 1,
            revision: 0,
            generation: 0,
            link_key: String::new(),
            mode: Mode::Observe,
            wan_upload_bps: None,
            wan_download_bps: None,
            device_from_android_bps: None,
            device_to_android_bps: None,
            grants: Vec::new(),
        }
    }
}

impl Config {
    pub fn parse(json: &[u8]) -> io::Result<Self> {
        if json.len() > MAX_LINE {
            return Err(invalid("configuration exceeds 4096 bytes"));
        }
        // Struct visitors reject duplicate known properties, unlike parsing into Value.
        let config: Self =
            serde_json::from_slice(json).map_err(|_| invalid("invalid configuration JSON"))?;
        config.validate()?;
        Ok(config)
    }

    pub fn validate(&self) -> io::Result<()> {
        if self.v != 1
            || self.revision == 0
            || self.generation == 0
            || self.revision > i64::MAX as u64
            || self.generation > i64::MAX as u64
            || self.link_key.is_empty()
            || self.link_key.len() > 256
            || self.link_key.chars().any(char::is_control)
        {
            return Err(invalid("invalid version, identity or revision"));
        }
        let caps = [
            self.wan_upload_bps,
            self.wan_download_bps,
            self.device_from_android_bps,
            self.device_to_android_bps,
        ];
        if caps.iter().flatten().any(|&bps| bps == 0 || bps > MAX_BPS) {
            return Err(invalid(
                "ceiling must be positive and at most 1250000000 B/s",
            ));
        }
        if self.shaping() && caps.iter().all(Option::is_none) {
            return Err(invalid("active mode requires a known ceiling"));
        }
        if self.grants.len() > 20 {
            return Err(invalid("too many grants"));
        }
        let mut keys = HashSet::new();
        let mut totals = HashMap::<(Domain, Direction), u64>::new();
        for grant in &self.grants {
            if grant.bytes_per_second > MAX_BPS
                || grant.burst_bytes == 0
                || grant.burst_bytes > 65536
                || !keys.insert((grant.domain, grant.direction, grant.traffic_class))
            {
                return Err(invalid("invalid or duplicate grant"));
            }
            let total = totals.entry((grant.domain, grant.direction)).or_default();
            *total = total
                .checked_add(grant.bytes_per_second)
                .ok_or_else(|| invalid("grant sum overflow"))?;
            if self
                .ceiling(grant.domain, grant.direction)
                .is_none_or(|cap| *total > cap)
            {
                return Err(invalid(
                    "grant requires and cannot exceed its domain ceiling",
                ));
            }
        }
        Ok(())
    }

    pub fn ceiling(&self, domain: Domain, direction: Direction) -> Option<u64> {
        match (domain, direction) {
            (Domain::Internet, Direction::FromAndroid) => self.wan_upload_bps,
            (Domain::Internet, Direction::ToAndroid) => self.wan_download_bps,
            (Domain::DeviceLink, Direction::FromAndroid) => self.device_from_android_bps,
            (Domain::DeviceLink, Direction::ToAndroid) => self.device_to_android_bps,
        }
    }
    pub fn shaping(&self) -> bool {
        matches!(self.mode, Mode::Manual | Mode::Adaptive)
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Ack {
    #[serde(flatten)]
    pub config: Config,
    pub accepted: bool,
    pub reason: Option<String>,
}

#[derive(Copy, Clone, Debug, Eq, PartialEq)]
pub struct Epoch {
    pub generation: u64,
    pub revision: u64,
}
type BucketKey = (Domain, Direction, Option<TrafficClass>);
type WaitKey = (Domain, Direction, TrafficClass);
struct Data {
    config: Config,
    buckets: HashMap<BucketKey, RateLimiter>,
    waiters: HashMap<WaitKey, VecDeque<u64>>,
    next_ticket: u64,
    sessions: Vec<Weak<SessionGeneration>>,
}

/// Every task in one DATA connection shares this identity. An unconfigured
/// connection adopts exactly the first accepted identity, inside apply's lock.
pub struct SessionGeneration {
    value: AtomicU64,
}

#[derive(Clone, Copy)]
enum Generation<'a> {
    #[cfg(test)]
    Fixed(u64),
    Session(&'a SessionGeneration),
}
impl Generation<'_> {
    fn current(self) -> u64 {
        match self {
            #[cfg(test)]
            Self::Fixed(value) => value,
            Self::Session(session) => session.value.load(Ordering::Acquire),
        }
    }
}

struct Waiter<'a> {
    state: &'a PolicyState,
    key: WaitKey,
    ticket: u64,
}
impl Drop for Waiter<'_> {
    fn drop(&mut self) {
        let mut data = self.state.data.lock().unwrap();
        if let Some(waiters) = data.waiters.get_mut(&self.key) {
            waiters.retain(|&ticket| ticket != self.ticket);
            if waiters.is_empty() {
                data.waiters.remove(&self.key);
            }
        }
        drop(data);
        self.state.serviced.notify_waiters();
    }
}

/// One shared governor per child process, used by every TCP and UDP proxy.
pub struct PolicyState {
    data: Mutex<Data>,
    changed: watch::Sender<Epoch>,
    origin: Instant,
    serviced: Notify,
}

impl PolicyState {
    pub fn new() -> Self {
        let (changed, _) = watch::channel(Epoch {
            generation: 0,
            revision: 0,
        });
        Self {
            data: Mutex::new(Data {
                config: Config::default(),
                buckets: HashMap::new(),
                waiters: HashMap::new(),
                next_ticket: 0,
                sessions: Vec::new(),
            }),
            changed,
            origin: Instant::now(),
            serviced: Notify::new(),
        }
    }
    pub fn subscribe(&self) -> watch::Receiver<Epoch> {
        self.changed.subscribe()
    }
    pub fn snapshot(&self) -> Config {
        self.data.lock().unwrap().config.clone()
    }
    pub fn bind_session(&self) -> Arc<SessionGeneration> {
        let mut data = self.data.lock().unwrap();
        let session = Arc::new(SessionGeneration {
            value: AtomicU64::new(data.config.generation),
        });
        data.sessions.retain(|session| session.strong_count() > 0);
        data.sessions.push(Arc::downgrade(&session));
        session
    }
    pub fn session_snapshot(&self, session: &SessionGeneration) -> io::Result<Config> {
        let data = self.data.lock().unwrap();
        if data.config.generation != session.value.load(Ordering::Acquire) {
            return Err(interrupted());
        }
        Ok(data.config.clone())
    }
    pub fn shaping(&self) -> bool {
        self.data.lock().unwrap().config.shaping()
    }
    pub fn elapsed(&self) -> Duration {
        self.origin.elapsed()
    }

    pub fn apply(&self, config: Config) -> Ack {
        if let Err(error) = config.validate() {
            return self.rejected(error.to_string());
        }
        let mut data = self.data.lock().unwrap();
        if config.generation < data.config.generation
            || (config.generation == data.config.generation
                && config.revision < data.config.revision)
        {
            return Ack {
                config: data.config.clone(),
                accepted: false,
                reason: Some("stale generation or revision".into()),
            };
        }
        if config.generation == data.config.generation && config.revision == data.config.revision {
            return Ack {
                accepted: config == data.config,
                reason: (config != data.config).then(|| "revision payload mismatch".into()),
                config: data.config.clone(),
            };
        }
        if config.generation == data.config.generation && config.link_key != data.config.link_key {
            return Ack {
                config: data.config.clone(),
                accepted: false,
                reason: Some("linkKey changed without new generation".into()),
            };
        }
        let now = self.elapsed();
        if config.generation != data.config.generation {
            data.buckets.clear();
            data.waiters.clear();
        }
        data.sessions.retain(|session| {
            if let Some(session) = session.upgrade() {
                let _ = session.value.compare_exchange(
                    0,
                    config.generation,
                    Ordering::AcqRel,
                    Ordering::Acquire,
                );
                true
            } else {
                false
            }
        });
        let mut limits = HashMap::new();
        if config.shaping() {
            for domain in [Domain::Internet, Domain::DeviceLink] {
                for direction in [Direction::FromAndroid, Direction::ToAndroid] {
                    if let Some(rate) = config.ceiling(domain, direction) {
                        limits.insert(
                            (domain, direction, None),
                            (rate, (rate / 200).clamp(1500, 65536)),
                        );
                    }
                }
            }
            for grant in &config.grants {
                limits.insert(
                    (grant.domain, grant.direction, Some(grant.traffic_class)),
                    (grant.bytes_per_second, grant.burst_bytes),
                );
            }
        }
        data.buckets.retain(|key, _| limits.contains_key(key));
        for (key, (rate, burst)) in limits {
            if let Some(bucket) = data.buckets.get_mut(&key) {
                bucket.update(rate, burst, now);
            } else {
                data.buckets.insert(key, RateLimiter::new(rate, burst, now));
            }
        }
        data.config = config;
        let epoch = Epoch {
            generation: data.config.generation,
            revision: data.config.revision,
        };
        let ack = Ack {
            config: data.config.clone(),
            accepted: true,
            reason: None,
        };
        drop(data);
        self.changed.send_replace(epoch);
        self.serviced.notify_waiters();
        ack
    }

    pub fn rejected(&self, reason: String) -> Ack {
        Ack {
            config: self.snapshot(),
            accepted: false,
            reason: Some(reason),
        }
    }

    fn keys(
        data: &Data,
        domain: Domain,
        direction: Direction,
        class: TrafficClass,
    ) -> Vec<BucketKey> {
        if !data.config.shaping() {
            return Vec::new();
        }
        let mut keys = Vec::with_capacity(2);
        if data.buckets.contains_key(&(domain, direction, None)) {
            keys.push((domain, direction, None));
        }
        if data.buckets.contains_key(&(domain, direction, Some(class))) {
            keys.push((domain, direction, Some(class)));
        }
        keys
    }

    /// Largest read or write the caller may use before the next debit. With no
    /// shaping bucket for this flow nothing is debited, so large reads are safe and
    /// avoid one syscall and one telemetry record per 1500 bytes. With shaping the
    /// chunk stays at or below the smallest burst, so a debit can always be satisfied.
    pub fn chunk_size(&self, domain: Domain, direction: Direction, class: TrafficClass) -> usize {
        let data = self.data.lock().unwrap();
        let keys = Self::keys(&data, domain, direction, class);
        if keys.is_empty() {
            return UNSHAPED_CHUNK;
        }
        keys.iter()
            .filter_map(|key| data.buckets.get(key))
            .map(RateLimiter::burst)
            .min()
            .unwrap_or(1500)
            .min(1500)
            .max(1)
    }

    #[cfg(test)]
    pub fn reserve_at(
        &self,
        domain: Domain,
        direction: Direction,
        class: TrafficClass,
        bytes: usize,
        now: Duration,
    ) -> Duration {
        let mut data = self.data.lock().unwrap();
        Self::keys(&data, domain, direction, class)
            .into_iter()
            .map(|key| data.buckets.get_mut(&key).unwrap().reserve(bytes, now))
            .max()
            .unwrap_or_default()
    }

    #[cfg(test)]
    pub async fn wait(
        &self,
        domain: Domain,
        direction: Direction,
        class: TrafficClass,
        bytes: usize,
        generation: u64,
        changed: &mut watch::Receiver<Epoch>,
    ) -> io::Result<()> {
        self.wait_core(
            domain,
            direction,
            class,
            bytes,
            Generation::Fixed(generation),
            changed,
            true,
        )
        .await
    }
    pub async fn wait_session(
        &self,
        domain: Domain,
        direction: Direction,
        class: TrafficClass,
        bytes: usize,
        session: &SessionGeneration,
        changed: &mut watch::Receiver<Epoch>,
    ) -> io::Result<()> {
        self.wait_core(
            domain,
            direction,
            class,
            bytes,
            Generation::Session(session),
            changed,
            true,
        )
        .await
    }
    pub async fn wait_available(
        &self,
        domain: Domain,
        direction: Direction,
        class: TrafficClass,
        bytes: usize,
        session: &SessionGeneration,
        changed: &mut watch::Receiver<Epoch>,
    ) -> io::Result<()> {
        self.wait_core(
            domain,
            direction,
            class,
            bytes,
            Generation::Session(session),
            changed,
            false,
        )
        .await
    }
    async fn wait_core(
        &self,
        domain: Domain,
        direction: Direction,
        class: TrafficClass,
        bytes: usize,
        generation: Generation<'_>,
        changed: &mut watch::Receiver<Epoch>,
        consume: bool,
    ) -> io::Result<()> {
        if bytes == 0 {
            return Ok(());
        }
        let key = (domain, direction, class);
        let ticket = {
            let mut data = self.data.lock().unwrap();
            if data.config.generation != generation.current() {
                return Err(interrupted());
            }
            data.next_ticket = data.next_ticket.wrapping_add(1);
            let ticket = data.next_ticket;
            data.waiters.entry(key).or_default().push_back(ticket);
            ticket
        };
        // Dropping a cancelled wait removes its ticket, so it cannot stall other flows.
        let _waiter = Waiter {
            state: self,
            key,
            ticket,
        };
        loop {
            let serviced = self.serviced.notified();
            tokio::pin!(serviced);
            serviced.as_mut().enable();
            let pause = {
                let mut data = self.data.lock().unwrap();
                if data.config.generation != generation.current() {
                    return Err(interrupted());
                }
                // When class grants are explicit, an omitted class has no residual permission.
                let scoped_grants = data
                    .config
                    .grants
                    .iter()
                    .any(|grant| grant.domain == domain && grant.direction == direction);
                let absent_class =
                    scoped_grants && !data.buckets.contains_key(&(domain, direction, Some(class)));
                let keys = Self::keys(&data, domain, direction, class);
                let now = self.elapsed();
                let at_front = data
                    .waiters
                    .get(&key)
                    .and_then(|waiters| waiters.front())
                    .is_some_and(|&first| first == ticket);
                let pause = if !at_front || (data.config.shaping() && absent_class) {
                    Duration::from_secs(86_400)
                } else {
                    keys.iter()
                        .map(|key| data.buckets.get_mut(key).unwrap().ready_in(bytes, now))
                        .max()
                        .unwrap_or_default()
                };
                if pause.is_zero() && consume {
                    for key in keys {
                        data.buckets.get_mut(&key).unwrap().reserve(bytes, now);
                    }
                }
                pause
            };
            if pause.is_zero() {
                return Ok(());
            }
            // No control mutex is held while sleeping. Policy updates wake every pending waiter.
            tokio::select! {
                _ = tokio::time::sleep(pause) => {},
                result = changed.changed() => { if result.is_err() { return Err(interrupted()); } },
                _ = &mut serviced => {},
            }
        }
    }
}

pub fn invalid(reason: &str) -> io::Error {
    io::Error::new(io::ErrorKind::InvalidData, reason)
}
pub fn interrupted() -> io::Error {
    io::Error::new(io::ErrorKind::Interrupted, "relay generation replaced")
}

/// Reads without allocating an unbounded String. Oversized lines are drained to LF.
pub async fn read_line_bounded<R: AsyncBufRead + Unpin>(
    reader: &mut R,
) -> io::Result<Option<Vec<u8>>> {
    let mut line = Vec::with_capacity(256);
    let mut oversized = false;
    loop {
        let chunk = reader.fill_buf().await?;
        if chunk.is_empty() {
            if line.is_empty() && !oversized {
                return Ok(None);
            }
            return Err(invalid("unterminated configuration line"));
        }
        let newline = chunk.iter().position(|&byte| byte == b'\n');
        let count = newline.map_or(chunk.len(), |index| index + 1);
        let data_count = count - usize::from(newline.is_some());
        if !oversized {
            if line.len() + data_count > MAX_LINE {
                oversized = true;
                line.clear();
            } else {
                line.extend_from_slice(&chunk[..data_count]);
            }
        }
        reader.consume(count);
        if newline.is_some() {
            if oversized {
                return Err(invalid("configuration exceeds 4096 bytes"));
            }
            if line.last() == Some(&b'\r') {
                line.pop();
            }
            return Ok(Some(line));
        }
    }
}
