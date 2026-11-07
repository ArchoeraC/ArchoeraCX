//! Coarse-grained global bandwidth limiter. Tokens are acquired after a chunk
//! is received and before it is written; reqwest's bounded body buffer supplies
//! the backpressure. A zero `bandwidthLimit` disables the limiter.

use std::sync::{Mutex, MutexGuard};
use std::time::{Duration, Instant};

pub(crate) struct BandwidthLimiter {
    rate: u64,
    state: Mutex<Bucket>,
}

struct Bucket {
    tokens: f64,
    last: Instant,
}

impl BandwidthLimiter {
    pub(crate) fn new(rate: u64) -> Option<Self> {
        if rate == 0 {
            return None;
        }
        let capacity = (rate / 5).max(1024 * 1024) as f64;
        Some(Self {
            rate,
            state: Mutex::new(Bucket {
                tokens: capacity,
                last: Instant::now(),
            }),
        })
    }

    pub(crate) async fn acquire(&self, bytes: u64) {
        if bytes == 0 {
            return;
        }
        let requested = bytes as f64;
        loop {
            let wait = {
                let mut bucket = lock(&self.state);
                let now = Instant::now();
                let elapsed = now.duration_since(bucket.last);
                bucket.last = now;
                let capacity = (self.rate / 5).max(1024 * 1024) as f64;
                bucket.tokens =
                    (bucket.tokens + elapsed.as_secs_f64() * self.rate as f64).min(capacity);
                if bucket.tokens >= requested {
                    bucket.tokens -= requested;
                    None
                } else {
                    let missing = requested - bucket.tokens;
                    Some(Duration::from_secs_f64(missing / self.rate as f64))
                }
            };
            match wait {
                Some(delay) => tokio::time::sleep(delay).await,
                None => return,
            }
        }
    }
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}
