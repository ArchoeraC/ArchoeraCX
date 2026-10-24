//! Bounded-concurrency executor: every planned file streams into
//! `<dest>.part`, is digest-verified, then atomically renamed into place.
//! Files at or above `rangeThreshold` fetch one connection per chunk
//! (`chunk_stream`, resumable through the `.part.meta` sidecar) and fall back
//! to a plain GET (`single_stream`) when a server ignores Range. Wire, resume
//! and outcome plumbing that both paths share lives in sibling modules.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use tokio::sync::Semaphore;
use tokio::task::{JoinError, JoinSet};
use tokio_util::sync::CancellationToken;
use url::Url;

use crate::chunk_stream::download_chunked;
use crate::client::build_client;
use crate::error::DownloadError;
use crate::limiter::BandwidthLimiter;
use crate::outcome::{FileOutcome, SourceFailure};
use crate::parts::{backoff, cleanup_part_with_meta, elapsed_ms, part_path};
use crate::plan::{ValidatedFile, validate_plan_inner};
use crate::progress::{FileMeter, ProgressCallback, ProgressTracker};
use crate::result::{DownloadResult, FailedFile, Status};
use crate::resume::{ChunkOutcome, remove_resume};
use crate::single_stream::{Existing, download_from_source, existing_destination, prepare_parent};

pub(crate) const MAX_ATTEMPTS_PER_SOURCE: u32 = 3;
const MAX_TOTAL_ATTEMPTS: u32 = 12;
const JOIN_GRACE: Duration = Duration::from_secs(2);

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
        host_gates: Mutex::new(HashMap::new()),
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
                        retryable: error.retryable(),
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

pub(crate) struct Engine {
    pub(crate) client: reqwest::Client,
    pub(crate) root: PathBuf,
    pub(crate) options: crate::plan::PlanOptions,
    pub(crate) limiter: Option<Arc<BandwidthLimiter>>,
    connections: Arc<Semaphore>,
    /// 0 disables the per-host cap.
    per_host: usize,
    /// One gate per host name so a hot CDN cannot starve the other hosts.
    host_gates: Mutex<HashMap<String, Arc<Semaphore>>>,
}

/// Global + per-host permits held for the duration of one HTTP response.
pub(crate) struct ConnectionGuard {
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
            // Poison is tolerated on purpose: this critical section is plain
            // synchronous map bookkeeping (no `.await`, no user code), so a
            // poisoning panic would have to land inside these few lines.
            // Recovering keeps the gate semaphores other tasks already share;
            // starting a fresh map instead would hand the same host a fresh
            // per-host budget while earlier tasks still hold their permits.
            // Logging the recovery would cost a new dependency, which this
            // crate deliberately avoids.
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

    /// Acquire a stream slot for one HTTP response, or report cancellation to
    /// the per-file retry loop; both download modes share this failure shape.
    pub(crate) async fn acquire_or_cancel(
        &self,
        url: &Url,
    ) -> Result<ConnectionGuard, SourceFailure> {
        self.acquire(url)
            .await
            .ok_or_else(|| SourceFailure::instant(DownloadError::Cancelled))
    }
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
                match download_chunked(Arc::clone(engine), file, source, &part, Arc::clone(meter)).await
                {
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
                    let retry_same_source = failure.error.retryable()
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
    let resumable = use_ranges && transferred > 0 && error.retryable();
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

fn accept_join(
    joined: Option<Result<(String, FileOutcome), JoinError>>,
    outcomes: &mut Vec<FileOutcome>,
) {
    match joined {
        Some(Ok((_, outcome))) => outcomes.push(outcome),
        Some(Err(error)) if error.is_cancelled() => {}
        Some(Err(error)) => outcomes.push(FileOutcome::task_failed(
            DownloadError::Internal(format!("download task failed: {error}")),
        )),
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
