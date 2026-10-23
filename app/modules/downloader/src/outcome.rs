//! Per-file outcomes and per-source failures shared by both download modes.

use crate::error::DownloadError;

/// Result for one planned file, aggregated in execution order.
pub(crate) struct FileOutcome {
    pub(crate) index: usize,
    pub(crate) dest: String,
    pub(crate) skipped: bool,
    pub(crate) cancelled: bool,
    /// Keep `<dest>.part` + `.part.meta` for a later resume.
    pub(crate) keep_part: bool,
    pub(crate) bytes: u64,
    pub(crate) attempts: u32,
    pub(crate) source: Option<String>,
    pub(crate) error: Option<DownloadError>,
}

impl FileOutcome {
    /// The fields every outcome starts from; constructors only fill deltas.
    fn fresh(index: usize, dest: String) -> Self {
        Self {
            index,
            dest,
            skipped: false,
            cancelled: false,
            keep_part: false,
            bytes: 0,
            attempts: 0,
            source: None,
            error: None,
        }
    }

    pub(crate) fn success(
        index: usize,
        dest: String,
        bytes: u64,
        attempts: u32,
        source: Option<String>,
    ) -> Self {
        let mut outcome = Self::fresh(index, dest);
        outcome.bytes = bytes;
        outcome.attempts = attempts;
        outcome.source = source;
        outcome
    }

    pub(crate) fn skipped(index: usize, dest: String) -> Self {
        let mut outcome = Self::fresh(index, dest);
        outcome.skipped = true;
        outcome
    }

    pub(crate) fn cancelled(index: usize, dest: String, bytes: u64) -> Self {
        let mut outcome = Self::fresh(index, dest);
        outcome.cancelled = true;
        outcome.bytes = bytes;
        outcome.error = Some(DownloadError::Cancelled);
        outcome
    }

    pub(crate) fn failed(
        index: usize,
        dest: String,
        error: DownloadError,
        bytes: u64,
        attempts: u32,
        source: Option<String>,
    ) -> Self {
        let mut outcome = Self::fresh(index, dest);
        outcome.bytes = bytes;
        outcome.attempts = attempts;
        outcome.source = source.filter(|value| !value.is_empty());
        outcome.error = Some(error);
        outcome
    }

    /// A download task that failed outside the per-file flow (panic or abort),
    /// reported against an unspecific slot by the executor's join loop.
    pub(crate) fn task_failed(error: DownloadError) -> Self {
        let mut outcome = Self::fresh(usize::MAX, "<download task>".into());
        outcome.error = Some(error);
        outcome
    }
}

/// Why one source attempt failed and how many bytes had already reached the
/// `.part` file when it did. The error type of every wire, stream and commit
/// step in both download modes.
pub(crate) struct SourceFailure {
    pub(crate) error: DownloadError,
    pub(crate) transferred: u64,
}

impl SourceFailure {
    /// A failure seen before any byte moved.
    pub(crate) fn instant(error: impl Into<DownloadError>) -> Self {
        Self {
            error: error.into(),
            transferred: 0,
        }
    }

    /// A failure seen after `transferred` bytes reached the target file.
    pub(crate) fn after(error: impl Into<DownloadError>, transferred: u64) -> Self {
        Self {
            error: error.into(),
            transferred,
        }
    }
}
