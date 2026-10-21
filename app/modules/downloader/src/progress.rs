//! Progress aggregation. Events are pushed by download tasks (never polled)
//! and throttled to `progressIntervalMs` (default 150 ms), with forced events
//! at start and completion.

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

/// Snapshot handed to the host callback. Field names mirror the C ABI struct
/// described in `docs/08-technical-research/05-download-engine.md` §1.3.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Progress {
    pub done_bytes: u64,
    pub total_bytes: u64,
    pub files_done: u64,
    pub files_total: u64,
    pub current_dest: Option<String>,
}

pub type ProgressCallback = Arc<dyn Fn(Progress) + Send + Sync>;

pub(crate) struct ProgressTracker {
    total_bytes: u64,
    files_total: u64,
    done_bytes: AtomicU64,
    files_done: AtomicU64,
    current_dest: Mutex<Option<String>>,
    last_emit: Mutex<Option<Instant>>,
    emitter: Mutex<()>,
    interval: Duration,
    callback: Option<ProgressCallback>,
}

impl ProgressTracker {
    pub(crate) fn new(
        total_bytes: u64,
        files_total: u64,
        interval_ms: u64,
        callback: Option<ProgressCallback>,
    ) -> Self {
        Self {
            total_bytes,
            files_total,
            done_bytes: AtomicU64::new(0),
            files_done: AtomicU64::new(0),
            current_dest: Mutex::new(None),
            last_emit: Mutex::new(None),
            emitter: Mutex::new(()),
            interval: Duration::from_millis(interval_ms.clamp(20, 5_000)),
            callback,
        }
    }

    pub(crate) fn set_current(&self, dest: &str) {
        *lock(&self.current_dest) = Some(dest.to_owned());
    }

    pub(crate) fn add_bytes(&self, bytes: u64) {
        if bytes == 0 {
            return;
        }
        self.done_bytes.fetch_add(bytes, Ordering::Relaxed);
        self.emit(false);
    }

    pub(crate) fn finish_file(&self, dest: &str) {
        self.files_done.fetch_add(1, Ordering::Relaxed);
        self.set_current(dest);
        self.emit(false);
    }

    pub(crate) fn emit_initial(&self) {
        self.emit(true);
    }

    pub(crate) fn finish_all(&self) {
        self.emit(true);
    }

    fn snapshot(&self) -> Progress {
        Progress {
            done_bytes: self.done_bytes.load(Ordering::Relaxed),
            total_bytes: self.total_bytes,
            files_done: self.files_done.load(Ordering::Relaxed),
            files_total: self.files_total,
            current_dest: lock(&self.current_dest).clone(),
        }
    }

    fn emit(&self, force: bool) {
        let Some(callback) = self.callback.as_ref() else {
            return;
        };

        {
            let mut last = lock(&self.last_emit);
            let now = Instant::now();
            if !force && last.is_some_and(|previous| now.duration_since(previous) < self.interval) {
                return;
            }
            *last = Some(now);
        }

        // Serialize snapshot + callback so events are delivered in
        // non-decreasing order even with concurrent file tasks.
        let _serial = lock(&self.emitter);
        let event = self.snapshot();
        // A host callback must not be able to abort the download by panicking.
        let _ = catch_unwind(AssertUnwindSafe(|| callback(event)));
    }
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Tracks a single file's contribution and caps it at `size`, so retries can
/// never make global progress exceed 100%.
pub(crate) struct FileMeter {
    size: u64,
    value: AtomicU64,
    tracker: Arc<ProgressTracker>,
}

impl FileMeter {
    pub(crate) fn new(size: u64, tracker: Arc<ProgressTracker>) -> Self {
        Self {
            size,
            value: AtomicU64::new(0),
            tracker,
        }
    }

    pub(crate) fn add(&self, bytes: u64) {
        advance(&self.value, &self.tracker, self.size, bytes);
    }

    /// Marks the file fully accounted for (success or skip).
    pub(crate) fn complete(&self) {
        let current = self.value.load(Ordering::Relaxed);
        if current < self.size {
            self.tracker.add_bytes(self.size - current);
            self.value.store(self.size, Ordering::Relaxed);
        }
    }
}

fn advance(value: &AtomicU64, tracker: &ProgressTracker, size: u64, bytes: u64) {
    let mut current = value.load(Ordering::Relaxed);
    loop {
        let next = current.saturating_add(bytes).min(size);
        match value.compare_exchange_weak(current, next, Ordering::Relaxed, Ordering::Relaxed) {
            Ok(_) => {
                tracker.add_bytes(next - current);
                return;
            }
            Err(observed) => current = observed,
        }
    }
}
