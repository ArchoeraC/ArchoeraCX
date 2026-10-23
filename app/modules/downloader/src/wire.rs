//! Wire helpers shared by the single-stream and ranged downloads: opening a
//! response with one request/send implementation, one shared status mapping,
//! content prechecks, and the streamed body write loop.

use std::time::Duration;

use reqwest::header::{ACCEPT_ENCODING, CONTENT_ENCODING, IF_RANGE, RANGE};
use tokio::io::AsyncWriteExt;
use url::Url;

use crate::client::READ_IDLE_TIMEOUT;
use crate::error::DownloadError;
use crate::executor::{ConnectionGuard, Engine};
use crate::hash::Hasher;
use crate::limiter::BandwidthLimiter;
use crate::outcome::SourceFailure;
use crate::plan::ValidatedFile;
use crate::progress::FileMeter;

/// Deadline for the response headers.
const HEADER_TIMEOUT: Duration = Duration::from_secs(20);

/// Send the GET for `file` against `source` — optionally ranged with an
/// `If-Range` validator — and return the response together with the
/// connection permits held for its body.
///
/// The caller must drop the guard before verification/commit so other streams
/// can use the budget again. Network and header-deadline failures are already
/// mapped; response status and body prechecks stay with the caller because
/// the two download modes disagree about which statuses are usable (see
/// [`status_failure`]).
pub(crate) async fn open_response(
    engine: &Engine,
    file: &ValidatedFile,
    source: &Url,
    range: Option<(u64, u64)>,
    if_range: Option<&str>,
    header_label: &str,
) -> Result<(reqwest::Response, ConnectionGuard), SourceFailure> {
    let guard = engine.acquire_or_cancel(source).await?;
    let mut request = engine
        .client
        .get(source.clone())
        .header(ACCEPT_ENCODING, "identity");
    if let Some((start, end)) = range {
        request = request.header(RANGE, format!("bytes={start}-{end}"));
        if let Some(validator) = if_range {
            request = request.header(IF_RANGE, validator);
        }
    }
    for (name, value) in &file.headers {
        request = request.header(name.clone(), value.clone());
    }

    let send = tokio::time::timeout(HEADER_TIMEOUT, request.send()).await;
    match send {
        Ok(Ok(response)) => Ok((response, guard)),
        Ok(Err(err)) => Err(SourceFailure::instant(err)),
        Err(_) => Err(SourceFailure::instant(DownloadError::Timeout(format!(
            "{header_label} timed out after {}s",
            HEADER_TIMEOUT.as_secs()
        )))),
    }
}

/// The status→failure mapping shared by both download modes: statuses that
/// are fatal no matter what the caller expected. Mode-specific handling (the
/// whole-body `200` of a plain GET, or `206` vs ignored `Range`) stays with
/// the callers.
pub(crate) fn status_failure(status: reqwest::StatusCode) -> Option<DownloadError> {
    if status == reqwest::StatusCode::TOO_MANY_REQUESTS {
        Some(DownloadError::Http429)
    } else if status.is_server_error() {
        Some(DownloadError::Http5xx {
            status: status.as_u16(),
        })
    } else if status.is_redirection() {
        Some(DownloadError::RedirectLoop)
    } else if !status.is_success() {
        Some(DownloadError::Http4xx {
            status: status.as_u16(),
        })
    } else {
        None
    }
}

/// The plan expects raw bytes, so any compressed response is rejected instead
/// of decoded (which would break both the length and digest checks).
pub(crate) fn encoding_rejection(response: &reqwest::Response) -> Option<DownloadError> {
    let raw = response.headers().get(CONTENT_ENCODING)?;
    let encoding = raw.to_str().unwrap_or("");
    if encoding.is_empty() || encoding.eq_ignore_ascii_case("identity") {
        None
    } else {
        Some(DownloadError::EncodingUnsupported(encoding.to_owned()))
    }
}

/// Reject a declared length that contradicts the plan before any byte
/// streams. Ranged requests validate `Content-Range` instead.
pub(crate) fn length_rejection(
    response: &reqwest::Response,
    expected: u64,
) -> Option<DownloadError> {
    let length = response.content_length()?;
    if length != expected {
        Some(DownloadError::SizeMismatch {
            expected,
            actual: length,
        })
    } else {
        None
    }
}

/// Stream the response body into `target` (already positioned), enforcing the
/// `expected` byte budget for this stream, the bandwidth limiter and the
/// per-file meter, then flush it to disk. `hasher` is updated per chunk when
/// the caller wants a stream-computed digest. Errors always report the plan
/// level `file.size` in a `SIZE_MISMATCH`, even when `expected` is a chunk
/// budget. Returns the written byte count.
pub(crate) async fn stream_body(
    mut response: reqwest::Response,
    file: &ValidatedFile,
    expected: u64,
    target: &mut tokio::fs::File,
    mut hasher: Option<&mut Hasher>,
    limiter: Option<&BandwidthLimiter>,
    meter: &FileMeter,
) -> Result<u64, SourceFailure> {
    let mut written = 0_u64;
    loop {
        let next = tokio::time::timeout(READ_IDLE_TIMEOUT, response.chunk()).await;
        let chunk = match next {
            Ok(Ok(chunk)) => chunk,
            Ok(Err(err)) => return Err(SourceFailure::after(err, written)),
            Err(_) => {
                return Err(SourceFailure::after(
                    DownloadError::Timeout(format!(
                        "read idle timeout after {}s",
                        READ_IDLE_TIMEOUT.as_secs()
                    )),
                    written,
                ));
            }
        };
        let Some(chunk) = chunk else {
            break;
        };
        let length = chunk.len() as u64;
        let next_written = written.saturating_add(length);
        if next_written > expected {
            return Err(SourceFailure::after(
                DownloadError::SizeMismatch {
                    expected: file.size,
                    actual: next_written,
                },
                written,
            ));
        }
        if let Some(limiter) = limiter {
            limiter.acquire(length).await;
        }
        if let Err(err) = target.write_all(&chunk).await {
            return Err(SourceFailure::after(err, written));
        }
        if let Some(hasher) = hasher.as_mut() {
            hasher.update(&chunk);
        }
        written = next_written;
        meter.add(length);
    }
    if written != expected {
        return Err(SourceFailure::after(
            DownloadError::SizeMismatch {
                expected: file.size,
                actual: written,
            },
            written,
        ));
    }
    if let Err(err) = target.sync_all().await {
        return Err(SourceFailure::after(err, written));
    }
    Ok(written)
}
