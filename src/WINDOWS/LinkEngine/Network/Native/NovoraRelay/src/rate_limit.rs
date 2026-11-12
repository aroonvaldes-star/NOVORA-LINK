use std::time::Duration;

const SCALE: i128 = 1_000_000_000;

/// Integer token accounting with a monotonic, caller-supplied clock.
/// Positive credit is bounded after idle; negative credit is already reserved service.
#[derive(Debug)]
pub struct RateLimiter {
    rate: u64,
    burst: u64,
    balance: i128,
    last: Duration,
}

impl RateLimiter {
    pub fn new(rate: u64, burst: u64, now: Duration) -> Self {
        Self {
            rate,
            burst,
            balance: burst as i128 * SCALE,
            last: now,
        }
    }

    fn refill(&mut self, now: Duration) {
        let elapsed = now
            .saturating_sub(self.last)
            .as_nanos()
            .min(i128::MAX as u128) as i128;
        self.balance = self
            .balance
            .saturating_add(elapsed.saturating_mul(self.rate as i128))
            .min(self.burst as i128 * SCALE);
        self.last = self.last.max(now);
    }

    fn debt_wait(&self, needed: i128) -> Duration {
        if needed <= 0 {
            return Duration::ZERO;
        }
        if self.rate == 0 {
            return Duration::from_secs(86_400);
        }
        let ns = (needed + self.rate as i128 - 1) / self.rate as i128;
        Duration::from_nanos(ns.min(u64::MAX as i128) as u64)
    }

    pub fn reserve(&mut self, bytes: usize, now: Duration) -> Duration {
        self.refill(now);
        if self.rate == 0 {
            return Duration::from_secs(86_400);
        }
        self.balance = self
            .balance
            .saturating_sub((bytes as i128).saturating_mul(SCALE));
        self.debt_wait(-self.balance)
    }

    pub fn ready_in(&mut self, bytes: usize, now: Duration) -> Duration {
        self.refill(now);
        if self.rate == 0 {
            return Duration::from_secs(86_400);
        }
        self.debt_wait(bytes as i128 * SCALE - self.balance)
    }

    pub fn update(&mut self, rate: u64, burst: u64, now: Duration) {
        self.refill(now);
        self.rate = rate;
        self.burst = burst;
        self.balance = self.balance.min(burst as i128 * SCALE);
    }

    #[cfg(test)]
    pub fn refund(&mut self, bytes: usize) {
        self.balance = self
            .balance
            .saturating_add(bytes as i128 * SCALE)
            .min(self.burst as i128 * SCALE);
    }

    pub fn burst(&self) -> usize {
        self.burst as usize
    }
}
