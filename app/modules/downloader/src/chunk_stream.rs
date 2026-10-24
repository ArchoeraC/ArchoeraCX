//! Ranged download: one HTTP connection per chunk, plus the fallback
//! decision-making. Resume logic lives in `resume.rs`.

use std::collections::BTreeSet;
use std::path::Path;
use std::sync::Arc;

use tokio::task::JoinSet;
use url::Url;

use crate::error::DownloadError;
use crate::executor::{Engine, MAX_ATTEMPTS_PER_SOURCE};
use crate::hash;
use crate::outcome::SourceFailure;
use crate::parts::{backoff, cleanup_part_with_meta, reject_link_like};
use crate::plan::{ValidatedFile, effective_chunk_size};
use crate::progress::FileMeter;
use crate::protocol::HttpVersion;
use crate::resume::{
    ChunkOutcome, ResumeMeta, RESUME_SCHEMA, content_range_matches, if_range_value, load_resume,
    remove_resume, save_resume,
};
use crate::single_stream::commit_part;
use crate::wire::{self, stream_body};

enum ChunkTaskError {
    Unsupported,
    Failed(SourceFailure),
}

enum FetchOutcome {
    Done {
        bytes: u64,
        etag: Option<String>,
        last_modified: Option<String>,
    },
    Unsupported,
}

/// Download `file` with one HTTP connection per chunk, resuming completed
/// chunks recorded in `<part>.meta` when the validator still matches.
///
/// Chunks share the engine's global stream budget and the per-host gate, so a
/// large file can use several streams without exceeding either limit. Every
/// chunk is verified by its `Content-Range`; a server that ignores Range (or a
/// changed validator) switches the whole file back to a single GET.
pub(crate) async fn download_chunked(
    engine: Arc<Engine>,
    file: &ValidatedFile,
    source: &Url,
    part: &Path,
    meter: Arc<FileMeter>,
) -> Result<ChunkOutcome, SourceFailure> {
    let chunk = effective_chunk_size(engine.options.chunk_size, file.size);
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
    let mut completed: BTreeSet<u32> = resumed
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
        reject_link_like(part)
            .await
            .map_err(SourceFailure::instant)?;
        let target = match tokio::fs::OpenOptions::new()
            .create(true)
            .write(true)
            .truncate(true)
            .open(part)
            .await
        {
            Ok(target) => target,
            Err(err) => return Err(SourceFailure::instant(err)),
        };
        if let Err(err) = target.set_len(file.size).await {
            return Err(SourceFailure::instant(err));
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
    // answer 200 and needlessly drop chunking). Recomputed after the probe:
    // a fresh download may have discovered the validator by then.
    let compute_if_range =
        |meta: &ResumeMeta| if resumed.is_some() { if_range_value(meta) } else { None };

    // Probe with the first missing chunk: starts the transfer and tells us
    // whether the server still honours Range for this validator.
    let probe_index = (0..ranges.len()).find(|index| !completed.contains(&(*index as u32)));
    let Some(probe_index) = probe_index else {
        // Everything is already on disk; verify and commit.
        let actual = hash::hash_file(part, file.algorithm)
            .await
            .map_err(SourceFailure::instant)?;
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
        compute_if_range(&meta).as_deref(),
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

    let if_range = compute_if_range(&meta);

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
                        let retryable = failure.error.retryable();
                        last_error = Some(failure);
                        if !retryable || attempt + 1 >= MAX_ATTEMPTS_PER_SOURCE {
                            break;
                        }
                        tokio::time::sleep(backoff(attempt)).await;
                    }
                }
            }
            Err(ChunkTaskError::Failed(last_error.unwrap_or_else(|| {
                SourceFailure::instant(DownloadError::Internal("chunk attempt failed".into()))
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
                return Err(SourceFailure::instant(DownloadError::Internal(format!(
                    "chunk task failed: {err}"
                ))));
            }
        }
    }

    let actual = hash::hash_file(part, file.algorithm)
        .await
        .map_err(SourceFailure::instant)?;
    commit_part(engine.as_ref(), file, part, &actual, transferred).await?;
    remove_resume(part).await;
    Ok(ChunkOutcome::Done(transferred))
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
    let (response, permit) = wire::open_response(
        engine,
        file,
        source,
        Some((start, end)),
        if_range,
        "range headers",
        // One chunk owns one connection; ranged requests never negotiate h2.
        HttpVersion::Http1,
    )
    .await?;

    let status = response.status();
    if status == reqwest::StatusCode::OK || status == reqwest::StatusCode::RANGE_NOT_SATISFIABLE {
        // Range ignored, or the validator changed and the server sent the
        // whole body: let the caller restart as a single stream.
        return Ok(FetchOutcome::Unsupported);
    }
    if let Some(error) = wire::status_failure(status) {
        let protocol = wire::protocol_status(status, response.version());
        return Err(SourceFailure::classified(error, protocol));
    }
    if status != reqwest::StatusCode::PARTIAL_CONTENT {
        return Err(SourceFailure::instant(DownloadError::Http4xx {
            status: status.as_u16(),
        }));
    }
    if let Some(error) = wire::encoding_rejection(&response) {
        return Err(SourceFailure::instant(error));
    }
    if !content_range_matches(response.headers(), start, end, file.size) {
        return Err(SourceFailure::instant(DownloadError::SizeMismatch {
            expected: file.size,
            actual: start,
        }));
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

    reject_link_like(part)
        .await
        .map_err(SourceFailure::instant)?;
    let mut target = match tokio::fs::OpenOptions::new().write(true).open(part).await {
        Ok(target) => target,
        Err(err) => return Err(SourceFailure::instant(err)),
    };
    if let Err(err) = tokio::io::AsyncSeekExt::seek(
        &mut target,
        std::io::SeekFrom::Start(start),
    )
    .await
    {
        return Err(SourceFailure::instant(err));
    }

    let written = stream_body(
        response,
        file,
        expected_length,
        &mut target,
        engine.limiter.as_deref(),
        meter,
    )
    .await?;
    drop(permit);

    Ok(FetchOutcome::Done {
        bytes: written,
        etag,
        last_modified,
    })
}
