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

const USER_AGENT: &str = concat!(
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

    let client = build_client()?;
    let limiter = BandwidthLimiter::new(validated.bandwidth_limit).map(Arc::new);
    let concurrency = validated.concurrency as usize;
    let engine = Arc::new(Engine {
        client,
        root: validated.root,
        options: validated.options,
        limiter,
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
}

struct FileOutcome {
    index: usize,
    dest: String,
    skipped: bool,
    cancelled: bool,
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
            cleanup_part(&part).await;
            FileOutcome::cancelled(file.index, file.dest.clone(), 0)
        }
    };

    if outcome.error.is_some() && !outcome.cancelled {
        cleanup_part(&part).await;
    }
    if outcome.skipped || (outcome.error.is_none() && !outcome.cancelled) {
        meter.complete();
        tracker.finish_file(&file.dest);
    }
    outcome
}

async fn download_file(engine: &Engine, file: &ValidatedFile, meter: &FileMeter) -> FileOutcome {
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

    'sources: for source in std::iter::once(&file.url).chain(file.mirrors.iter()) {
        last_source = Some(source.as_str().to_owned());
        for attempt in 0..MAX_ATTEMPTS_PER_SOURCE {
            if attempts >= MAX_TOTAL_ATTEMPTS {
                break 'sources;
            }
            attempts += 1;
            match download_from_source(engine, file, source, &part, meter).await {
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

    FileOutcome::failed(
        file.index,
        file.dest.clone(),
        last_error.unwrap_or_else(|| DownloadError::Internal("no source attempted".into())),
        transferred,
        attempts,
        last_source,
    )
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

    let actual = hasher.finalize_hex();
    if actual != file.hash_hex {
        return Err(SourceFailure {
            error: DownloadError::HashMismatch {
                expected: format!("{}:{}", file.algorithm.name(), file.hash_hex),
                actual,
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

    Ok(transferred)
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

fn build_client() -> Result<reqwest::Client, DownloadError> {
    reqwest::Client::builder()
        .user_agent(USER_AGENT)
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
        }))
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
