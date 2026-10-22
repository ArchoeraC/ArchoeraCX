//! Bounded-concurrency executor: stream to `<dest>.part`, verify, then atomically
//! rename. No Range/chunking yet (v1 single-stream GET).

use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{Duration, Instant};

use reqwest::header::{ACCEPT_ENCODING, CONTENT_ENCODING};
use reqwest::redirect::Policy;
use tokio::sync::Semaphore;
use tokio::task::{JoinError, JoinSet};
use tokio_util::sync::CancellationToken;
use url::Url;

use crate::error::DownloadError;
use crate::hash;
use crate::limiter::BandwidthLimiter;
use crate::plan::{
    Durability, FileMode, ValidatedFile, VerifyExisting, is_link_like, is_loopback,
    validate_plan_inner,
};
use crate::progress::{FileMeter, ProgressCallback, ProgressTracker};
use crate::result::{DownloadResult, FailedFile, Status};

pub(crate) const USER_AGENT: &str = concat!(
    "ArchoeraCX/",
    env!("CARGO_PKG_VERSION"),
    " (+https://archoera.invalid; contact)"
);
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);
const HEADER_TIMEOUT: Duration = Duration::from_secs(20);
const READ_IDLE_TIMEOUT: Duration = Duration::from_secs(30);
const JOIN_GRACE: Duration = Duration::from_secs(2);
const MAX_ATTEMPTS_PER_SOURCE: u32 = 2;
const MAX_TOTAL_ATTEMPTS: u32 = 12;

pub async fn execute(
    plan: crate::plan::DownloadPlan,
    cancel: CancellationToken,
    progress: Option<ProgressCallback>,
) -> Result<DownloadResult, DownloadError> {
    let validated = validate_plan_inner(plan)?;
    let started = Instant::now();
    let plan_id = validated.plan_id.clone();
    let tracker = Arc::new(ProgressTracker::new(
        validated.total_bytes,
        validated.files.len() as u64,
        validated.options.progress_interval_ms,
        progress,
    ));

    if let Some(first) = validated.files.first() {
        tracker.set_current(&first.dest);
    }
    tracker.emit_initial();

    if cancel.is_cancelled() {
        return Ok(cancelled_result(plan_id, started));
    }
    if validated.files.is_empty() {
        tracker.finish_all();
        return Ok(DownloadResult {
            plan_id,
            status: Status::Completed,
            ok: 0,
            skipped: 0,
            failed: Vec::new(),
            bytes: 0,
            elapsed_ms: elapsed_ms(started),
            post: Vec::new(),
        });
    }

    // Ranged files are the same host as the first URL, so one sample is
    // enough for the edge probe.
    let edge_sample = validated
        .files
        .iter()
        .map(|file| file.url.clone())
        .find(|url| url.host_str() == Some(crate::edge::EDGE_HOST));
    let client = build_client(&validated.options, edge_sample.as_ref()).await?;
    let limiter = BandwidthLimiter::new(validated.bandwidth_limit).map(Arc::new);
    let concurrency = validated.concurrency as usize;
    let per_host = validated.options.per_host_concurrency as usize;
    let engine = Arc::new(Engine {
        client,
        root: validated.root,
        options: validated.options,
        limiter,
        // One budget for every HTTP stream: single-stream files take one
        // permit, ranged files take one per chunk. File slots are counted
        // separately so a ranged file never blocks its own chunk workers.
        connections: Arc::new(Semaphore::new(concurrency.max(1))),
        per_host,
        host_gates: std::sync::Mutex::new(std::collections::HashMap::new()),
    });

    let semaphore = Arc::new(Semaphore::new(concurrency));
    let jobs = validated.files;
    let mut join_set: JoinSet<(String, FileOutcome)> = JoinSet::new();
    let mut outcomes: Vec<FileOutcome> = Vec::with_capacity(jobs.len());
    let mut next_job = 0_usize;
    let mut cancelled = false;

    loop {
        if next_job < jobs.len() {
            tokio::select! {
                biased;
                _ = cancel.cancelled() => {
                    cancelled = true;
                    break;
                }
                joined = join_set.join_next(), if !join_set.is_empty() => {
                    accept_join(joined, &mut outcomes);
                }
                permit = Arc::clone(&semaphore).acquire_owned() => {
                    let permit = match permit {
                        Ok(permit) => permit,
                        Err(_) => {
                            cancelled = true;
                            break;
                        }
                    };
                    let job = jobs[next_job].clone();
                    next_job += 1;
                    let engine = Arc::clone(&engine);
                    let token = cancel.clone();
                    let tracker = Arc::clone(&tracker);
                    let task_dest = job.dest.clone();
                    join_set.spawn(async move {
                        let _permit = permit;
                        let outcome = run_job(engine, job, token, tracker).await;
                        (task_dest, outcome)
                    });
                }
            }
        } else if !join_set.is_empty() {
            tokio::select! {
                biased;
                _ = cancel.cancelled() => {
                    cancelled = true;
                    break;
                }
                joined = join_set.join_next() => {
                    accept_join(joined, &mut outcomes);
                }
            }
        } else {
            break;
        }
    }

    if cancelled {
        cancel.cancel();
        drain_after_cancel(&mut join_set, &mut outcomes).await;
    }

    tracker.finish_all();

    let mut ok = 0_u64;
    let mut skipped_count = 0_u64;
    let mut bytes = 0_u64;
    let mut failed = Vec::new();
    let mut failures: Vec<(usize, FailedFile)> = Vec::new();

    for outcome in outcomes {
        bytes = bytes.saturating_add(outcome.bytes);
        if outcome.cancelled {
            continue;
        }
        if outcome.skipped {
            skipped_count += 1;
            continue;
        }
        match outcome.error {
            None => ok += 1,
            Some(error) => {
                let code = error.code();
                failures.push((
                    outcome.index,
                    FailedFile {
                        dest: outcome.dest,
                        code: code.as_str().to_owned(),
                        source: outcome.source,
                        attempts: outcome.attempts,
                        retryable: code.retryable(),
                        message: error.to_string(),
                    },
                ));
            }
        }
    }
    failures.sort_by_key(|(index, _)| *index);
    failed.extend(failures.into_iter().map(|(_, failure)| failure));

    let status = if cancelled {
        Status::Cancelled
    } else if failed.is_empty() {
        Status::Completed
    } else if ok == 0 && skipped_count == 0 {
        Status::Failed
    } else {
        Status::Partial
    };

    Ok(DownloadResult {
        plan_id,
        status,
        ok,
        skipped: skipped_count,
        failed,
        bytes,
        elapsed_ms: elapsed_ms(started),
        post: Vec::new(),
    })
}

struct Engine {
    client: reqwest::Client,
    root: PathBuf,
    options: crate::plan::PlanOptions,
    limiter: Option<Arc<BandwidthLimiter>>,
    connections: Arc<Semaphore>,
    /// 0 disables the per-host cap.
    per_host: usize,
    host_gates: std::sync::Mutex<std::collections::HashMap<String, Arc<Semaphore>>>,
}

/// Global + per-host permits held for the duration of one HTTP response.
struct ConnectionGuard {
    _global: tokio::sync::OwnedSemaphorePermit,
    _host: Option<tokio::sync::OwnedSemaphorePermit>,
}

impl Engine {
    /// Acquire the global stream budget first, then the host gate, so two
    /// tasks can never hold each other's host while waiting for the global
    /// budget (no lock-order inversion, no deadlock).
    async fn acquire(&self, url: &Url) -> Option<ConnectionGuard> {
        let global = Arc::clone(&self.connections).acquire_owned().await.ok()?;
        let host = url.host_str().unwrap_or_default().to_ascii_lowercase();
        let gate = {
            let mut gates = self
                .host_gates
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            Arc::clone(
                gates
                    .entry(host)
                    .or_insert_with(|| Arc::new(Semaphore::new(self.per_host.max(1)))),
            )
        };
        if self.per_host == 0 {
            return Some(ConnectionGuard {
                _global: global,
                _host: None,
            });
        }
        let host_permit = gate.acquire_owned().await.ok()?;
        Some(ConnectionGuard {
            _global: global,
            _host: Some(host_permit),
        })
    }
}

struct FileOutcome {
    index: usize,
    dest: String,
    skipped: bool,
    cancelled: bool,
    /// Keep `<dest>.part` + `.part.meta` for a later resume.
    keep_part: bool,
    bytes: u64,
    attempts: u32,
    source: Option<String>,
    error: Option<DownloadError>,
}

impl FileOutcome {
    fn success(
        index: usize,
        dest: String,
        bytes: u64,
        attempts: u32,
        source: Option<String>,
    ) -> Self {
        Self {
            index,
            dest,
            skipped: false,
            cancelled: false,
            keep_part: false,
            bytes,
            attempts,
            source,
            error: None,
        }
    }

    fn skipped(index: usize, dest: String) -> Self {
        Self {
            index,
            dest,
            skipped: true,
            cancelled: false,
            keep_part: false,
            bytes: 0,
            attempts: 0,
            source: None,
            error: None,
        }
    }

    fn cancelled(index: usize, dest: String, bytes: u64) -> Self {
        Self {
            index,
            dest,
            skipped: false,
            cancelled: true,
            keep_part: false,
            bytes,
            attempts: 0,
            source: None,
            error: Some(DownloadError::Cancelled),
        }
    }

    fn failed(
        index: usize,
        dest: String,
        error: DownloadError,
        bytes: u64,
        attempts: u32,
        source: Option<String>,
    ) -> Self {
        Self {
            index,
            dest,
            skipped: false,
            cancelled: false,
            keep_part: false,
            bytes,
            attempts,
            source: source.filter(|value| !value.is_empty()),
            error: Some(error),
        }
    }
}

struct SourceFailure {
    error: DownloadError,
    transferred: u64,
}

async fn run_job(
    engine: Arc<Engine>,
    file: ValidatedFile,
    cancel: CancellationToken,
    tracker: Arc<ProgressTracker>,
) -> FileOutcome {
    tracker.set_current(&file.dest);
    let meter = Arc::new(FileMeter::new(file.size, Arc::clone(&tracker)));
    let part = part_path(&file.dest_path);

    let outcome = tokio::select! {
        biased;
        outcome = download_file(&engine, &file, &meter) => outcome,
        _ = cancel.cancelled() => {
            cleanup_part_with_meta(&part).await;
            FileOutcome::cancelled(file.index, file.dest.clone(), 0)
        }
    };

    if outcome.error.is_some() && !outcome.cancelled && !outcome.keep_part {
        cleanup_part_with_meta(&part).await;
    }
    if outcome.skipped {
        remove_resume(&part).await;
    }
    if outcome.skipped || (outcome.error.is_none() && !outcome.cancelled) {
        meter.complete();
        tracker.finish_file(&file.dest);
    }
    outcome
}

async fn download_file(
    engine: &Arc<Engine>,
    file: &ValidatedFile,
    meter: &Arc<FileMeter>,
) -> FileOutcome {
    match existing_destination(engine, file).await {
        Ok(Existing::Skip) => return FileOutcome::skipped(file.index, file.dest.clone()),
        Ok(Existing::Download) => {}
        Err(error) => {
            return FileOutcome::failed(file.index, file.dest.clone(), error, 0, 0, None);
        }
    }

    if let Err(error) = prepare_parent(engine, &file.dest_path).await {
        return FileOutcome::failed(file.index, file.dest.clone(), error, 0, 0, None);
    }

    let part = part_path(&file.dest_path);
    let mut transferred = 0_u64;
    let mut attempts = 0_u32;
    let mut last_error: Option<DownloadError> = None;
    let mut last_source: Option<String> = None;

    // Large files are fetched with parallel Range requests (one connection
    // each) instead of a single stream; servers that ignore Range fall back
    // to the plain GET below.
    let use_ranges = engine.options.chunk_size > 0
        && file.size >= engine.options.range_threshold
        && file.size > engine.options.chunk_size;

    'sources: for source in std::iter::once(&file.url).chain(file.mirrors.iter()) {
        last_source = Some(source.as_str().to_owned());
        for attempt in 0..MAX_ATTEMPTS_PER_SOURCE {
            if attempts >= MAX_TOTAL_ATTEMPTS {
                break 'sources;
            }
            attempts += 1;
            let outcome = if use_ranges {
                match download_chunked(Arc::clone(engine), file, source, &part, Arc::clone(meter)).await {
                    Ok(ChunkOutcome::Done(downloaded)) => Ok(downloaded),
                    Ok(ChunkOutcome::Unsupported) => {
                        // Server ignored the range: restart as one stream.
                        cleanup_part_with_meta(&part).await;
                        download_from_source(engine.as_ref(), file, source, &part, meter.as_ref()).await
                    }
                    Err(failure) => Err(failure),
                }
            } else {
                download_from_source(engine.as_ref(), file, source, &part, meter.as_ref()).await
            };
            match outcome {
                Ok(downloaded) => {
                    transferred = transferred.saturating_add(downloaded);
                    return FileOutcome::success(
                        file.index,
                        file.dest.clone(),
                        transferred,
                        attempts,
                        last_source,
                    );
                }
                Err(failure) => {
                    transferred = transferred.saturating_add(failure.transferred);
                    let retry_same_source = failure.error.code().retryable()
                        && !matches!(failure.error, DownloadError::HashMismatch { .. })
                        && attempt + 1 < MAX_ATTEMPTS_PER_SOURCE
                        && attempts < MAX_TOTAL_ATTEMPTS;
                    let local = failure.error.is_local_failure();
                    last_error = Some(failure.error);
                    if local {
                        break 'sources;
                    }
                    if retry_same_source {
                        tokio::time::sleep(backoff(attempt)).await;
                        continue;
                    }
                    break;
                }
            }
        }
    }

    let error =
        last_error.unwrap_or_else(|| DownloadError::Internal("no source attempted".into()));
    let resumable = use_ranges && transferred > 0 && error.code().retryable();
    let mut outcome = FileOutcome::failed(
        file.index,
        file.dest.clone(),
        error,
        transferred,
        attempts,
        last_source,
    );
    outcome.keep_part = resumable;
    outcome
}

enum Existing {
    Skip,
    Download,
}

async fn existing_destination(
    engine: &Engine,
    file: &ValidatedFile,
) -> Result<Existing, DownloadError> {
    let metadata = match tokio::fs::symlink_metadata(&file.dest_path).await {
        Ok(metadata) => metadata,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(Existing::Download),
        Err(err) => return Err(err.into()),
    };

    if is_link_like(&metadata) {
        return Err(DownloadError::PathEscape(format!(
            "{}: destination is a symlink or reparse point",
            file.dest
        )));
    }
    if metadata.is_dir() {
        return Err(DownloadError::DestIsDir(file.dest.clone()));
    }

    if file.mode == FileMode::Skip {
        return Ok(Existing::Skip);
    }
    if file.mode == FileMode::Replace {
        return Ok(Existing::Download);
    }
    if file.mode != FileMode::Ensure {
        return Err(DownloadError::InvalidPlan(format!(
            "{}: unsupported file mode",
            file.dest
        )));
    }

    match engine.options.verify_existing {
        VerifyExisting::None => Ok(Existing::Skip),
        VerifyExisting::Size => {
            if metadata.len() == file.size {
                Ok(Existing::Skip)
            } else {
                Ok(Existing::Download)
            }
        }
        VerifyExisting::Hash => {
            if metadata.len() != file.size {
                return Ok(Existing::Download);
            }
            let actual = hash::hash_file(&file.dest_path, file.algorithm).await?;
            if actual == file.hash_hex {
                Ok(Existing::Skip)
            } else {
                Ok(Existing::Download)
            }
        }
    }
}

async fn prepare_parent(engine: &Engine, dest: &Path) -> Result<(), DownloadError> {
    let parent = dest
        .parent()
        .ok_or_else(|| DownloadError::InvalidPlan("destination has no parent".into()))?;
    tokio::fs::create_dir_all(parent).await?;
    let canonical = tokio::fs::canonicalize(parent).await?;
    if !canonical.starts_with(&engine.root) {
        return Err(DownloadError::PathEscape(format!(
            "{} escapes download root",
            dest.display()
        )));
    }
    Ok(())
}

async fn download_from_source(
    engine: &Engine,
    file: &ValidatedFile,
    source: &Url,
    part: &Path,
    meter: &FileMeter,
) -> Result<u64, SourceFailure> {
    let mut transferred = 0_u64;
    let permit = match engine.acquire(source).await {
        Some(permit) => permit,
        None => {
            return Err(SourceFailure {
                error: DownloadError::Cancelled,
                transferred: 0,
            });
        }
    };
    let mut request = engine
        .client
        .get(source.clone())
        .header(ACCEPT_ENCODING, "identity");
    for (name, value) in &file.headers {
        request = request.header(name.clone(), value.clone());
    }

    let send = tokio::time::timeout(HEADER_TIMEOUT, request.send()).await;
    let response = match send {
        Ok(Ok(response)) => response,
        Ok(Err(err)) => {
            return Err(SourceFailure {
                error: DownloadError::from_reqwest(err),
                transferred: 0,
            });
        }
        Err(_) => {
            return Err(SourceFailure {
                error: DownloadError::Timeout(format!(
                    "response headers timed out after {}s",
                    HEADER_TIMEOUT.as_secs()
                )),
                transferred: 0,
            });
        }
    };

    let status = response.status();
    if status == reqwest::StatusCode::TOO_MANY_REQUESTS {
        return Err(SourceFailure {
            error: DownloadError::Http429,
            transferred: 0,
        });
    }
    if status.is_server_error() {
        return Err(SourceFailure {
            error: DownloadError::Http5xx {
                status: status.as_u16(),
            },
            transferred: 0,
        });
    }
    if status.is_redirection() {
        return Err(SourceFailure {
            error: DownloadError::RedirectLoop,
            transferred: 0,
        });
    }
    if !status.is_success() {
        return Err(SourceFailure {
            error: DownloadError::Http4xx {
                status: status.as_u16(),
            },
            transferred: 0,
        });
    }

    if let Some(encoding) = response.headers().get(CONTENT_ENCODING) {
        let encoding = encoding.to_str().unwrap_or("");
        if !encoding.is_empty() && !encoding.eq_ignore_ascii_case("identity") {
            return Err(SourceFailure {
                error: DownloadError::EncodingUnsupported(encoding.to_owned()),
                transferred: 0,
            });
        }
    }
    if let Some(length) = response.content_length() {
        if length != file.size {
            return Err(SourceFailure {
                error: DownloadError::SizeMismatch {
                    expected: file.size,
                    actual: length,
                },
                transferred: 0,
            });
        }
    }

    let mut target = tokio::fs::File::create(part)
        .await
        .map_err(|err| SourceFailure {
            error: err.into(),
            transferred: 0,
        })?;
    let mut response = response;
    let mut hasher = file.algorithm.hasher();
    let mut written = 0_u64;

    loop {
        let next = tokio::time::timeout(READ_IDLE_TIMEOUT, response.chunk()).await;
        let chunk = match next {
            Ok(Ok(chunk)) => chunk,
            Ok(Err(err)) => {
                return Err(SourceFailure {
                    error: DownloadError::from_reqwest(err),
                    transferred,
                });
            }
            Err(_) => {
                return Err(SourceFailure {
                    error: DownloadError::Timeout(format!(
                        "read idle timeout after {}s",
                        READ_IDLE_TIMEOUT.as_secs()
                    )),
                    transferred,
                });
            }
        };
        let Some(chunk) = chunk else {
            break;
        };
        let length = chunk.len() as u64;
        let next_written = written.saturating_add(length);
        if next_written > file.size {
            return Err(SourceFailure {
                error: DownloadError::SizeMismatch {
                    expected: file.size,
                    actual: next_written,
                },
                transferred,
            });
        }
        if let Some(limiter) = &engine.limiter {
            limiter.acquire(length).await;
        }
        if let Err(err) = tokio::io::AsyncWriteExt::write_all(&mut target, &chunk).await {
            return Err(SourceFailure {
                error: err.into(),
                transferred,
            });
        }
        hasher.update(&chunk);
        written = next_written;
        transferred = transferred.saturating_add(length);
        meter.add(length);
    }

    if written != file.size {
        return Err(SourceFailure {
            error: DownloadError::SizeMismatch {
                expected: file.size,
                actual: written,
            },
            transferred,
        });
    }
    if let Err(err) = target.sync_all().await {
        return Err(SourceFailure {
            error: err.into(),
            transferred,
        });
    }
    drop(target);

    drop(permit);

    let actual = hasher.finalize_hex();
    commit_part(engine, file, part, &actual, transferred).await?;

    Ok(transferred)
}

/// Compare the digest and atomically move `<dest>.part` into place.
async fn commit_part(
    engine: &Engine,
    file: &ValidatedFile,
    part: &Path,
    actual: &str,
    transferred: u64,
) -> Result<(), SourceFailure> {
    if actual != file.hash_hex {
        return Err(SourceFailure {
            error: DownloadError::HashMismatch {
                expected: format!("{}:{}", file.algorithm.name(), file.hash_hex),
                actual: actual.to_owned(),
            },
            transferred,
        });
    }

    if let Err(error) = rename_with_retry(part, &file.dest_path).await {
        return Err(SourceFailure { error, transferred });
    }

    if engine.options.durability == Durability::Full {
        if let Some(parent) = file.dest_path.parent() {
            if let Ok(directory) = tokio::fs::File::open(parent).await {
                let _ = directory.sync_all().await;
            }
        }
    }

    Ok(())
}

/// Outcome of a ranged attempt for one source.
enum ChunkOutcome {
    Done(u64),
    /// The server answered a Range request with the whole body (or with a
    /// different validator); the caller should restart as a single stream.
    Unsupported,
}

/// `.part.meta` sidecar: lets an interrupted large download resume only the
/// chunks that are still missing. Validators must match before resuming.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct ResumeMeta {
    schema_version: u32,
    size: u64,
    hash_algo: String,
    hash_hex: String,
    chunk_size: u64,
    #[serde(default)]
    etag: Option<String>,
    #[serde(default)]
    last_modified: Option<String>,
    #[serde(default)]
    completed: Vec<u32>,
}

const RESUME_SCHEMA: u32 = 1;

fn meta_path(part: &Path) -> PathBuf {
    let mut value = part.as_os_str().to_os_string();
    value.push(".meta");
    PathBuf::from(value)
}

async fn load_resume(part: &Path, file: &ValidatedFile, chunk_size: u64) -> Option<ResumeMeta> {
    let path = meta_path(part);
    let bytes = tokio::fs::read(&path).await.ok()?;
    let meta: ResumeMeta = serde_json::from_slice(&bytes).ok()?;
    if meta.schema_version != RESUME_SCHEMA
        || meta.size != file.size
        || meta.chunk_size != chunk_size
        || meta.hash_algo != file.algorithm.name()
        || !meta.hash_hex.eq_ignore_ascii_case(&file.hash_hex)
    {
        return None;
    }
    // Validators are optional, but the part file itself must be complete size.
    let metadata = tokio::fs::metadata(part).await.ok()?;
    if metadata.len() != file.size {
        return None;
    }
    Some(meta)
}

async fn save_resume(part: &Path, meta: &ResumeMeta) {
    let path = meta_path(part);
    let temporary = path.with_extension("meta.tmp");
    let Ok(bytes) = serde_json::to_vec(meta) else {
        return;
    };
    if tokio::fs::write(&temporary, bytes).await.is_ok() {
        let _ = tokio::fs::rename(&temporary, &path).await;
    }
}

async fn remove_resume(part: &Path) {
    let _ = tokio::fs::remove_file(meta_path(part)).await;
}

/// Remove the data file and its resume sidecar.
async fn cleanup_part_with_meta(part: &Path) {
    remove_resume(part).await;
    cleanup_part(part).await;
}

/// Download `file` with one HTTP connection per chunk, resuming completed
/// chunks recorded in `<part>.meta` when the validator still matches.
///
/// Chunks share the engine's global stream budget and the per-host gate, so a
/// large file can use several streams without exceeding either limit. Every
/// chunk is verified by its `Content-Range`; a server that ignores Range (or a
/// changed validator) switches the whole file back to a single GET.
async fn download_chunked(
    engine: Arc<Engine>,
    file: &ValidatedFile,
    source: &Url,
    part: &Path,
    meter: Arc<FileMeter>,
) -> Result<ChunkOutcome, SourceFailure> {
    let chunk = engine.options.chunk_size.max(1);
    let mut ranges: Vec<(u64, u64)> = Vec::new();
    let mut start = 0_u64;
    while start < file.size {
        let end = start.saturating_add(chunk - 1).min(file.size - 1);
        ranges.push((start, end));
        start = end + 1;
    }
    if ranges.len() < 2 {
        return Ok(ChunkOutcome::Unsupported);
    }

    let resumed = load_resume(part, file, chunk).await;
    let mut completed: std::collections::BTreeSet<u32> = resumed
        .as_ref()
        .map(|meta| {
            meta.completed
                .iter()
                .copied()
                .filter(|index| (*index as usize) < ranges.len())
                .collect()
        })
        .unwrap_or_default();

    let mut meta = ResumeMeta {
        schema_version: RESUME_SCHEMA,
        size: file.size,
        hash_algo: file.algorithm.name().to_owned(),
        hash_hex: file.hash_hex.clone(),
        chunk_size: chunk,
        etag: resumed.as_ref().and_then(|meta| meta.etag.clone()),
        last_modified: resumed.as_ref().and_then(|meta| meta.last_modified.clone()),
        completed: completed.iter().copied().collect(),
    };

    if resumed.is_none() {
        // Fresh download: truncate any stale data and reserve the final size.
        let target = match tokio::fs::OpenOptions::new()
            .create(true)
            .write(true)
            .truncate(true)
            .open(part)
            .await
        {
            Ok(target) => target,
            Err(err) => {
                return Err(SourceFailure {
                    error: err.into(),
                    transferred: 0,
                });
            }
        };
        if let Err(err) = target.set_len(file.size).await {
            return Err(SourceFailure {
                error: err.into(),
                transferred: 0,
            });
        }
        drop(target);
        completed.clear();
        meta.completed.clear();
        save_resume(part, &meta).await;
    } else {
        // Count the resumed bytes once so progress reflects what is on disk.
        let mut already = 0_u64;
        for index in &completed {
            let (start, end) = ranges[*index as usize];
            already = already.saturating_add(end - start + 1);
        }
        meter.add(already);
    }

    // `If-Range` only matters when bytes are already on disk; a fresh download
    // must not send it (an unquoted ETag from some CDNs would make the server
    // answer 200 and needlessly drop chunking).
    let if_range = if resumed.is_some() {
        if_range_value(&meta)
    } else {
        None
    };

    // Probe with the first missing chunk: starts the transfer and tells us
    // whether the server still honours Range for this validator.
    let probe_index = (0..ranges.len())
        .find(|index| !completed.contains(&(*index as u32)));
    let Some(probe_index) = probe_index else {
        // Everything is already on disk; verify and commit.
        let actual = hash::hash_file(part, file.algorithm)
            .await
            .map_err(|error| SourceFailure {
                error,
                transferred: 0,
            })?;
        commit_part(engine.as_ref(), file, part, &actual, 0).await?;
        remove_resume(part).await;
        return Ok(ChunkOutcome::Done(0));
    };

    let mut transferred = match fetch_range(
        engine.as_ref(),
        file,
        source,
        ranges[probe_index],
        part,
        meter.as_ref(),
        if_range.as_deref(),
    )
    .await
    {
        Ok(FetchOutcome::Done {
            bytes,
            etag,
            last_modified,
        }) => {
            if meta.etag.is_none() {
                meta.etag = etag;
            }
            if meta.last_modified.is_none() {
                meta.last_modified = last_modified;
            }
            completed.insert(probe_index as u32);
            meta.completed = completed.iter().copied().collect();
            save_resume(part, &meta).await;
            bytes
        }
        Ok(FetchOutcome::Unsupported) => {
            cleanup_part_with_meta(part).await;
            return Ok(ChunkOutcome::Unsupported);
        }
        Err(failure) => return Err(failure),
    };

    let if_range = if resumed.is_some() {
        if_range_value(&meta)
    } else {
        None
    };

    let mut join_set: JoinSet<Result<(u32, u64), ChunkTaskError>> = JoinSet::new();
    for (index, range) in ranges.iter().copied().enumerate() {
        if completed.contains(&(index as u32)) {
            continue;
        }
        let engine = Arc::clone(&engine);
        let file = file.clone();
        let source = source.clone();
        let part = part.to_path_buf();
        let meter = Arc::clone(&meter);
        let if_range = if_range.clone();
        join_set.spawn(async move {
            let mut last_error: Option<SourceFailure> = None;
            // A dropped connection only costs this chunk, so retry it before
            // giving up on the whole file/source.
            for attempt in 0..MAX_ATTEMPTS_PER_SOURCE {
                match fetch_range(
                    &engine,
                    &file,
                    &source,
                    range,
                    &part,
                    &meter,
                    if_range.as_deref(),
                )
                .await
                {
                    Ok(FetchOutcome::Done { bytes, .. }) => return Ok((index as u32, bytes)),
                    Ok(FetchOutcome::Unsupported) => return Err(ChunkTaskError::Unsupported),
                    Err(failure) => {
                        let retryable = failure.error.code().retryable();
                        last_error = Some(failure);
                        if !retryable || attempt + 1 >= MAX_ATTEMPTS_PER_SOURCE {
                            break;
                        }
                        tokio::time::sleep(backoff(attempt)).await;
                    }
                }
            }
            Err(ChunkTaskError::Failed(last_error.unwrap_or_else(|| SourceFailure {
                error: DownloadError::Internal("chunk attempt failed".into()),
                transferred: 0,
            })))
        });
    }

    while let Some(joined) = join_set.join_next().await {
        match joined {
            Ok(Ok((index, bytes))) => {
                completed.insert(index);
                meta.completed = completed.iter().copied().collect();
                save_resume(part, &meta).await;
                transferred = transferred.saturating_add(bytes);
            }
            Ok(Err(ChunkTaskError::Unsupported)) => {
                // The server sent a full body for a ranged chunk (validator or
                // range support changed); restart this file as one stream.
                join_set.abort_all();
                cleanup_part_with_meta(part).await;
                return Ok(ChunkOutcome::Unsupported);
            }
            Ok(Err(ChunkTaskError::Failed(failure))) => {
                join_set.abort_all();
                return Err(failure);
            }
            Err(err) => {
                join_set.abort_all();
                return Err(SourceFailure {
                    error: DownloadError::Internal(format!("chunk task failed: {err}")),
                    transferred,
                });
            }
        }
    }

    let actual = hash::hash_file(part, file.algorithm)
        .await
        .map_err(|error| SourceFailure {
            error,
            transferred,
        })?;
    commit_part(engine.as_ref(), file, part, &actual, transferred).await?;
    remove_resume(part).await;
    Ok(ChunkOutcome::Done(transferred))
}

enum ChunkTaskError {
    Unsupported,
    Failed(SourceFailure),
}

/// `If-Range` accepts a strong ETag or an HTTP date; weak ETags (`W/...`) must
/// be skipped, and Last-Modified is the fallback.
fn if_range_value(meta: &ResumeMeta) -> Option<String> {
    // A strong validator must be a quoted entity-tag. Unquoted values (seen
    // from some object stores) and weak tags are not usable with If-Range, so
    // fall back to Last-Modified.
    let strong = meta.etag.as_deref().filter(|etag| {
        let etag = etag.trim();
        etag.len() >= 2
            && etag.starts_with('"')
            && etag.ends_with('"')
            && !etag.starts_with("W/")
    });
    strong
        .map(|etag| etag.trim().to_owned())
        .or_else(|| meta.last_modified.clone())
}

enum FetchOutcome {
    Done {
        bytes: u64,
        etag: Option<String>,
        last_modified: Option<String>,
    },
    Unsupported,
}

/// Fetch one byte range and write it at its offset in `part`.
#[allow(clippy::too_many_arguments)]
async fn fetch_range(
    engine: &Engine,
    file: &ValidatedFile,
    source: &Url,
    (start, end): (u64, u64),
    part: &Path,
    meter: &FileMeter,
    if_range: Option<&str>,
) -> Result<FetchOutcome, SourceFailure> {
    let expected_length = end - start + 1;
    let permit = match engine.acquire(source).await {
        Some(permit) => permit,
        None => {
            return Err(SourceFailure {
                error: DownloadError::Cancelled,
                transferred: 0,
            });
        }
    };

    let mut request = engine
        .client
        .get(source.clone())
        .header(ACCEPT_ENCODING, "identity")
        .header(reqwest::header::RANGE, format!("bytes={start}-{end}"));
    if let Some(validator) = if_range {
        request = request.header(reqwest::header::IF_RANGE, validator);
    }
    for (name, value) in &file.headers {
        request = request.header(name.clone(), value.clone());
    }

    let send = tokio::time::timeout(HEADER_TIMEOUT, request.send()).await;
    let response = match send {
        Ok(Ok(response)) => response,
        Ok(Err(err)) => {
            return Err(SourceFailure {
                error: DownloadError::from_reqwest(err),
                transferred: 0,
            });
        }
        Err(_) => {
            return Err(SourceFailure {
                error: DownloadError::Timeout(format!(
                    "range headers timed out after {}s",
                    HEADER_TIMEOUT.as_secs()
                )),
                transferred: 0,
            });
        }
    };

    let status = response.status();
    if status == reqwest::StatusCode::OK || status == reqwest::StatusCode::RANGE_NOT_SATISFIABLE {
        // Range ignored, or the validator changed and the server sent the
        // whole body: let the caller restart as a single stream.
        return Ok(FetchOutcome::Unsupported);
    }
    if status == reqwest::StatusCode::TOO_MANY_REQUESTS {
        return Err(SourceFailure {
            error: DownloadError::Http429,
            transferred: 0,
        });
    }
    if status.is_server_error() {
        return Err(SourceFailure {
            error: DownloadError::Http5xx {
                status: status.as_u16(),
            },
            transferred: 0,
        });
    }
    if status.is_redirection() {
        return Err(SourceFailure {
            error: DownloadError::RedirectLoop,
            transferred: 0,
        });
    }
    if status != reqwest::StatusCode::PARTIAL_CONTENT {
        return Err(SourceFailure {
            error: DownloadError::Http4xx {
                status: status.as_u16(),
            },
            transferred: 0,
        });
    }

    if let Some(encoding) = response.headers().get(CONTENT_ENCODING) {
        let encoding = encoding.to_str().unwrap_or("");
        if !encoding.is_empty() && !encoding.eq_ignore_ascii_case("identity") {
            return Err(SourceFailure {
                error: DownloadError::EncodingUnsupported(encoding.to_owned()),
                transferred: 0,
            });
        }
    }
    if !content_range_matches(response.headers(), start, end, file.size) {
        return Err(SourceFailure {
            error: DownloadError::SizeMismatch {
                expected: file.size,
                actual: start,
            },
            transferred: 0,
        });
    }

    let etag = response
        .headers()
        .get(reqwest::header::ETAG)
        .and_then(|value| value.to_str().ok())
        .map(|value| value.trim().to_owned())
        // Only quoted, strong entity-tags are valid If-Range validators.
        .filter(|value| value.starts_with('"') && value.ends_with('"'));
    let last_modified = response
        .headers()
        .get(reqwest::header::LAST_MODIFIED)
        .and_then(|value| value.to_str().ok())
        .map(|value| value.to_owned());

    let mut target = match tokio::fs::OpenOptions::new()
        .write(true)
        .open(part)
        .await
    {
        Ok(target) => target,
        Err(err) => {
            return Err(SourceFailure {
                error: err.into(),
                transferred: 0,
            });
        }
    };
    if let Err(err) = tokio::io::AsyncSeekExt::seek(
        &mut target,
        std::io::SeekFrom::Start(start),
    )
    .await
    {
        return Err(SourceFailure {
            error: err.into(),
            transferred: 0,
        });
    }

    let mut response = response;
    let mut written = 0_u64;
    loop {
        let next = tokio::time::timeout(READ_IDLE_TIMEOUT, response.chunk()).await;
        let chunk = match next {
            Ok(Ok(chunk)) => chunk,
            Ok(Err(err)) => {
                return Err(SourceFailure {
                    error: DownloadError::from_reqwest(err),
                    transferred: written,
                });
            }
            Err(_) => {
                return Err(SourceFailure {
                    error: DownloadError::Timeout(format!(
                        "read idle timeout after {}s",
                        READ_IDLE_TIMEOUT.as_secs()
                    )),
                    transferred: written,
                });
            }
        };
        let Some(chunk) = chunk else {
            break;
        };
        let length = chunk.len() as u64;
        let next_written = written.saturating_add(length);
        if next_written > expected_length {
            return Err(SourceFailure {
                error: DownloadError::SizeMismatch {
                    expected: file.size,
                    actual: next_written,
                },
                transferred: written,
            });
        }
        if let Some(limiter) = &engine.limiter {
            limiter.acquire(length).await;
        }
        if let Err(err) =
            tokio::io::AsyncWriteExt::write_all(&mut target, &chunk).await
        {
            return Err(SourceFailure {
                error: err.into(),
                transferred: written,
            });
        }
        written = next_written;
        meter.add(length);
    }

    if written != expected_length {
        return Err(SourceFailure {
            error: DownloadError::SizeMismatch {
                expected: file.size,
                actual: written,
            },
            transferred: written,
        });
    }
    if let Err(err) = target.sync_all().await {
        return Err(SourceFailure {
            error: err.into(),
            transferred: written,
        });
    }
    drop(permit);

    Ok(FetchOutcome::Done {
        bytes: written,
        etag,
        last_modified,
    })
}

/// Validate `Content-Range: bytes <start>-<end>/<total>` for a chunk.
fn content_range_matches(
    headers: &reqwest::header::HeaderMap,
    start: u64,
    end: u64,
    total: u64,
) -> bool {
    let Some(value) = headers
        .get(reqwest::header::CONTENT_RANGE)
        .and_then(|value| value.to_str().ok())
    else {
        return false;
    };
    let value = value.trim();
    let Some(rest) = value.strip_prefix("bytes ") else {
        return false;
    };
    let Some((range, size)) = rest.split_once('/') else {
        return false;
    };
    let Some((got_start, got_end)) = range.split_once('-') else {
        return false;
    };
    if size.trim() != "*" && size.trim() != total.to_string() {
        return false;
    }
    got_start.trim() == start.to_string() && got_end.trim() == end.to_string()
}

async fn rename_with_retry(part: &Path, dest: &Path) -> Result<(), DownloadError> {
    let mut delay = Duration::from_millis(100);
    let mut last_error = None;
    for attempt in 0..5 {
        match tokio::fs::rename(part, dest).await {
            Ok(()) => return Ok(()),
            Err(err) => {
                let error = DownloadError::from_io(err);
                let locked = matches!(error, DownloadError::FileLocked(_));
                last_error = Some(error);
                if !locked || attempt == 4 {
                    break;
                }
                tokio::time::sleep(delay).await;
                delay = delay.saturating_mul(2);
            }
        }
    }
    Err(last_error
        .unwrap_or_else(|| DownloadError::Internal("rename failed without an error".into())))
}

async fn build_client(
    options: &crate::plan::PlanOptions,
    edge_sample: Option<&Url>,
) -> Result<reqwest::Client, DownloadError> {
    let mut builder = reqwest::Client::builder()
        .user_agent(USER_AGENT)
        // One stream per TCP connection: h2 multiplexes every request onto a
        // single connection, where per-connection throttling and flow control
        // cap the aggregate speed. Launchers win their throughput from many
        // independent connections instead.
        .http1_only()
        .connect_timeout(CONNECT_TIMEOUT)
        .read_timeout(READ_IDLE_TIMEOUT)
        .tcp_keepalive(Duration::from_secs(15))
        .tcp_nodelay(true)
        .pool_idle_timeout(Duration::from_secs(30))
        .pool_max_idle_per_host(8)
        .http2_adaptive_window(true)
        .redirect(Policy::custom(|attempt| {
            let too_many = attempt.previous().len() >= 5;
            let allowed = is_redirect_allowed(attempt.url());
            if too_many {
                attempt.error(std::io::Error::other("too many redirects"))
            } else if allowed {
                attempt.follow()
            } else {
                attempt.error(std::io::Error::other("redirect scheme rejected"))
            }
        }));

    // The proxy is resolved by the host process (C#) and handed over
    // explicitly; the executor never reads environment proxies on its own.
    if let Some(proxy) = &options.proxy {
        let mut configured = reqwest::Proxy::all(proxy.url.trim()).map_err(|err| {
            DownloadError::InvalidPlan(format!("proxy url is invalid: {err}"))
        })?;
        if !proxy.no_proxy.is_empty() {
            if let Some(no_proxy) = reqwest::NoProxy::from_string(&proxy.no_proxy.join(",")) {
                configured = configured.no_proxy(Some(no_proxy));
            }
        }
        builder = builder.proxy(configured);
    } else if let Some(sample) = edge_sample {
        // No proxy: pick the Fastly edge that actually delivers on this
        // network instead of trusting one DNS answer (see edge.rs).
        let edges = crate::edge::modrinth_edges(sample).await;
        if !edges.is_empty() {
            builder = builder.resolve_to_addrs(crate::edge::EDGE_HOST, &edges);
        }
    }

    builder
        .build()
        .map_err(|err| DownloadError::Internal(format!("HTTP client build failed: {err}")))
}

fn is_redirect_allowed(url: &Url) -> bool {
    if !url.username().is_empty() || url.password().is_some() {
        return false;
    }
    match url.scheme() {
        "https" => true,
        "http" => is_loopback(url),
        _ => false,
    }
}

fn part_path(dest: &Path) -> PathBuf {
    let mut value = dest.as_os_str().to_os_string();
    value.push(".part");
    PathBuf::from(value)
}

async fn cleanup_part(part: &Path) {
    match tokio::fs::remove_file(part).await {
        Ok(()) => {}
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => {}
        Err(_) => {}
    }
}

fn backoff(attempt: u32) -> Duration {
    let millis = 500_u64.saturating_mul(1_u64 << attempt.min(6));
    Duration::from_millis(millis.min(5_000))
}

fn accept_join(
    joined: Option<Result<(String, FileOutcome), JoinError>>,
    outcomes: &mut Vec<FileOutcome>,
) {
    match joined {
        Some(Ok((_, outcome))) => outcomes.push(outcome),
        Some(Err(error)) if error.is_cancelled() => {}
        Some(Err(error)) => outcomes.push(FileOutcome {
            index: usize::MAX,
            dest: "<download task>".into(),
            skipped: false,
            cancelled: false,
            keep_part: false,
            bytes: 0,
            attempts: 0,
            source: None,
            error: Some(DownloadError::Internal(format!(
                "download task failed: {error}"
            ))),
        }),
        None => {}
    }
}

async fn drain_after_cancel(
    join_set: &mut JoinSet<(String, FileOutcome)>,
    outcomes: &mut Vec<FileOutcome>,
) {
    let deadline = Instant::now() + JOIN_GRACE;
    while !join_set.is_empty() {
        let now = Instant::now();
        if now >= deadline {
            join_set.abort_all();
            while let Some(joined) = join_set.join_next().await {
                accept_join(Some(joined), outcomes);
            }
            break;
        }
        let remaining = deadline.duration_since(now);
        match tokio::time::timeout(remaining, join_set.join_next()).await {
            Ok(Some(joined)) => accept_join(Some(joined), outcomes),
            Ok(None) => break,
            Err(_) => {
                join_set.abort_all();
                while let Some(joined) = join_set.join_next().await {
                    accept_join(Some(joined), outcomes);
                }
                break;
            }
        }
    }
}

fn cancelled_result(plan_id: String, started: Instant) -> DownloadResult {
    DownloadResult {
        plan_id,
        status: Status::Cancelled,
        ok: 0,
        skipped: 0,
        failed: Vec::new(),
        bytes: 0,
        elapsed_ms: elapsed_ms(started),
        post: Vec::new(),
    }
}

fn elapsed_ms(started: Instant) -> u64 {
    started.elapsed().as_millis().min(u128::from(u64::MAX)) as u64
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn part_path_is_sibling() {
        let dest = Path::new("/tmp/root/mods/a.jar");
        assert_eq!(part_path(dest), Path::new("/tmp/root/mods/a.jar.part"));
    }

    #[test]
    fn backoff_is_capped() {
        assert_eq!(backoff(0), Duration::from_millis(500));
        assert!(backoff(20) <= Duration::from_secs(5));
    }

    #[test]
    fn error_code_mapping_for_local_failure() {
        assert_eq!(crate::error::ErrorCode::DiskFull.as_str(), "DISK_FULL");
    }
}
