//! `.part.meta` sidecar for ranged downloads and the outcome of a ranged
//! attempt.
//!
//! The sidecar lets an interrupted large download resume only the chunks that
//! are still missing. Validators must match before resuming.

use std::path::{Path, PathBuf};

use crate::plan::ValidatedFile;

/// The resume sidecar beside a `.part` file.
fn meta_path(part: &Path) -> PathBuf {
    let mut value = part.as_os_str().to_os_string();
    value.push(".meta");
    PathBuf::from(value)
}

/// Sidecar state for one ranged download: which chunks are complete and the
/// validators that made them so.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct ResumeMeta {
    pub(crate) schema_version: u32,
    pub(crate) size: u64,
    pub(crate) hash_algo: String,
    pub(crate) hash_hex: String,
    pub(crate) chunk_size: u64,
    #[serde(default)]
    pub(crate) etag: Option<String>,
    #[serde(default)]
    pub(crate) last_modified: Option<String>,
    #[serde(default)]
    pub(crate) completed: Vec<u32>,
}

pub(crate) const RESUME_SCHEMA: u32 = 1;
/// A sidecar with more than this is corrupt or hostile; ignore it and restart
/// instead of reading an unbounded file into memory.
const MAX_RESUME_META_BYTES: u64 = 64 * 1024;

pub(crate) async fn load_resume(
    part: &Path,
    file: &ValidatedFile,
    chunk_size: u64,
) -> Option<ResumeMeta> {
    let path = meta_path(part);
    let metadata = tokio::fs::symlink_metadata(&path).await.ok()?;
    if !metadata.is_file() || metadata.len() > MAX_RESUME_META_BYTES {
        return None;
    }
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

pub(crate) async fn save_resume(part: &Path, meta: &ResumeMeta) {
    let path = meta_path(part);
    let temporary = path.with_extension("meta.tmp");
    if crate::parts::reject_link_like(&temporary).await.is_err() {
        return;
    }
    let Ok(bytes) = serde_json::to_vec(meta) else {
        return;
    };
    if tokio::fs::write(&temporary, bytes).await.is_ok() {
        let _ = tokio::fs::rename(&temporary, &path).await;
    }
}

pub(crate) async fn remove_resume(part: &Path) {
    let _ = tokio::fs::remove_file(meta_path(part)).await;
}

/// Result of a ranged attempt for one source.
pub(crate) enum ChunkOutcome {
    /// The file is committed; `u64` bytes were transferred in this attempt.
    Done(u64),
    /// The server answered a Range request with the whole body (or with a
    /// different validator); the caller should restart as a single stream.
    Unsupported,
}

/// `If-Range` accepts a strong ETag or an HTTP date; weak ETags (`W/...`) must
/// be skipped, and Last-Modified is the fallback.
pub(crate) fn if_range_value(meta: &ResumeMeta) -> Option<String> {
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

/// Validate `Content-Range: bytes <start>-<end>/<total>` for a chunk.
pub(crate) fn content_range_matches(
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
