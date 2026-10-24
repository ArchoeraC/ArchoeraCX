//! `DownloadPlan` model and full pre-flight validation.
//!
//! The plan is the only input to the downloader. Validation happens before a
//! single byte is written; failures are reported as `INVALID_PLAN`,
//! `PATH_ESCAPE`, `INVALID_HASH` or `DUPLICATE_DEST` (docs/10 §9.2–9.3).

use std::collections::{BTreeMap, HashSet};
use std::path::{Component, Path, PathBuf};
use std::str::FromStr;

use reqwest::header::{HeaderName, HeaderValue};
use serde::{Deserialize, Serialize};
use url::{Host, Url};

use crate::error::DownloadError;
use crate::hash::HashAlgorithm;

pub const SCHEMA_VERSION: u32 = 1;
pub const MAX_PLAN_JSON_BYTES: usize = 64 * 1024 * 1024;
pub const MAX_FILES: usize = 200_000;
pub const MAX_FILE_BYTES: u64 = 1 << 40; // 1 TiB
/// Parallel streams (files, or range chunks within one large file).
/// 16 keeps many small files latency-bound-free without flooding a link.
pub const DEFAULT_CONCURRENCY: u32 = 16;
pub const MIN_CONCURRENCY: u32 = 1;
pub const MAX_CONCURRENCY: u32 = 64;
/// Default per-host stream cap. Prism/MultiMC and the design note settle on a
/// single-digit budget; a hot CDN answers more reliably to 8 independent
/// connections than to a 16-way handshake storm.
pub const DEFAULT_PER_HOST_CONCURRENCY: u32 = 8;
pub const MAX_PER_HOST_CONCURRENCY: u32 = 64;
/// Only genuinely large files use ranged chunks. Minecraft's assets are mostly
/// a few KiB and libraries mostly single-digit MiB; chunking them multiplies
/// requests and TLS handshakes without raising throughput.
pub const DEFAULT_RANGE_THRESHOLD: u64 = 8 * 1024 * 1024;
/// One ranged request. 4 MiB keeps the request count near a single stream while
/// still letting a large file borrow several connections.
pub const DEFAULT_CHUNK_SIZE: u64 = 4 * 1024 * 1024;
/// No sensible plan wants smaller chunks: the request count (and the task
/// count) grows inversely with the chunk size. The downloader's own tests use
/// 64 KiB, which is accepted.
pub const MIN_CHUNK_SIZE: u64 = 64 * 1024;
/// A single range response should stay small enough to buffer/retry cheaply.
pub const MAX_CHUNK_SIZE: u64 = 64 * 1024 * 1024;
/// Hard cap on ranges per file. A larger file grows its chunk instead of its
/// task count: a 1 TiB file gets ~1 GiB chunks, not 262k requests.
pub const MAX_CHUNKS_PER_FILE: u64 = 1024;
const MAX_DEST_BYTES: usize = 4_096;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DownloadPlan {
    #[serde(default = "default_schema_version")]
    pub schema_version: u32,
    #[serde(default)]
    pub plan_id: String,
    #[serde(default)]
    pub root: String,
    #[serde(default = "default_concurrency")]
    pub concurrency: u32,
    #[serde(default)]
    pub priority: i32,
    #[serde(default)]
    pub bandwidth_limit: u64,
    #[serde(default)]
    pub total_bytes: u64,
    #[serde(default)]
    pub options: PlanOptions,
    #[serde(default)]
    pub files: Vec<FileSpec>,
    /// Compatibility only: v1 never executes post actions.
    #[serde(default)]
    pub post: Vec<serde_json::Value>,
}

impl DownloadPlan {
    /// Parse and validate a plan from UTF-8 JSON bytes.
    pub fn from_json_slice(bytes: &[u8]) -> Result<Self, DownloadError> {
        if bytes.len() > MAX_PLAN_JSON_BYTES {
            return Err(DownloadError::PlanTooLarge(format!(
                "plan JSON is {} bytes, limit is {MAX_PLAN_JSON_BYTES}",
                bytes.len()
            )));
        }
        let plan: Self = serde_json::from_slice(bytes)?;
        validate_plan(&plan)?;
        Ok(plan)
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlanOptions {
    #[serde(default = "default_true")]
    pub dedup: bool,
    #[serde(default)]
    pub verify_existing: VerifyExisting,
    #[serde(default)]
    pub durability: Durability,
    #[serde(default = "default_range_threshold")]
    pub range_threshold: u64,
    #[serde(default = "default_chunk_size")]
    pub chunk_size: u64,
    #[serde(default = "default_progress_interval_ms")]
    pub progress_interval_ms: u64,
    /// Streams allowed per host name; 0 disables the per-host cap.
    #[serde(default = "default_per_host_concurrency")]
    pub per_host_concurrency: u32,
    /// Explicit proxy resolved by the host (C#); the executor never guesses.
    #[serde(default)]
    pub proxy: Option<ProxySpec>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProxySpec {
    /// `http(s)://` or `socks5(h)://` proxy URL, optionally with userinfo.
    pub url: String,
    /// Host suffixes that bypass the proxy.
    #[serde(default)]
    pub no_proxy: Vec<String>,
}

impl Default for PlanOptions {
    fn default() -> Self {
        Self {
            dedup: true,
            verify_existing: VerifyExisting::Size,
            durability: Durability::Normal,
            range_threshold: default_range_threshold(),
            chunk_size: default_chunk_size(),
            progress_interval_ms: default_progress_interval_ms(),
            per_host_concurrency: default_per_host_concurrency(),
            proxy: None,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum VerifyExisting {
    #[default]
    Size,
    Hash,
    None,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Durability {
    #[default]
    Normal,
    Full,
    Fast,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FileSpec {
    pub url: String,
    #[serde(default)]
    pub mirrors: Vec<String>,
    /// Canonical hash object. Legacy `sha1`/`sha256`/`sha512` fields are also
    /// accepted and normalized during validation.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub hash: Option<HashSpec>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sha1: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sha256: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sha512: Option<String>,
    #[serde(default)]
    pub size: u64,
    pub dest: String,
    #[serde(default)]
    pub mode: FileMode,
    #[serde(default)]
    pub priority: i32,
    #[serde(default)]
    pub executable: bool,
    #[serde(default)]
    pub headers: BTreeMap<String, String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HashSpec {
    pub algo: String,
    pub hex: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum FileMode {
    #[default]
    Ensure,
    Replace,
    Link,
    Symlink,
    Skip,
}

/// A parsed, normalized file entry. Not serializable: it contains resolved
/// URLs, absolute paths and header values.
#[derive(Debug, Clone)]
pub(crate) struct ValidatedFile {
    pub index: usize,
    pub url: Url,
    pub mirrors: Vec<Url>,
    pub algorithm: HashAlgorithm,
    pub hash_hex: String,
    pub size: u64,
    pub dest: String,
    pub dest_path: PathBuf,
    pub mode: FileMode,
    pub priority: i32,
    pub headers: Vec<(HeaderName, HeaderValue)>,
}

#[derive(Debug, Clone)]
pub(crate) struct ValidatedPlan {
    pub plan_id: String,
    pub root: PathBuf,
    pub concurrency: u32,
    pub bandwidth_limit: u64,
    pub total_bytes: u64,
    pub options: PlanOptions,
    pub files: Vec<ValidatedFile>,
}

/// Validate a plan without starting any download.
pub fn validate_plan(plan: &DownloadPlan) -> Result<(), DownloadError> {
    validate_plan_inner(plan.clone()).map(|_| ())
}

pub(crate) fn validate_plan_inner(plan: DownloadPlan) -> Result<ValidatedPlan, DownloadError> {
    if plan.schema_version != SCHEMA_VERSION {
        return Err(DownloadError::InvalidPlan(format!(
            "unsupported schemaVersion {} (expected {SCHEMA_VERSION})",
            plan.schema_version
        )));
    }
    if plan.plan_id.trim().is_empty() {
        return Err(DownloadError::InvalidPlan("planId is required".into()));
    }
    if plan.plan_id.len() > 256 {
        return Err(DownloadError::InvalidPlan("planId is too long".into()));
    }
    if !(MIN_CONCURRENCY..=MAX_CONCURRENCY).contains(&plan.concurrency) {
        return Err(DownloadError::InvalidPlan(format!(
            "concurrency {} is outside {MIN_CONCURRENCY}..={MAX_CONCURRENCY}",
            plan.concurrency
        )));
    }
    if plan.options.per_host_concurrency > MAX_PER_HOST_CONCURRENCY {
        return Err(DownloadError::InvalidPlan(format!(
            "options.perHostConcurrency {} exceeds {MAX_PER_HOST_CONCURRENCY}",
            plan.options.per_host_concurrency
        )));
    }
    if plan.options.chunk_size != 0
        && !(MIN_CHUNK_SIZE..=MAX_CHUNK_SIZE).contains(&plan.options.chunk_size)
    {
        return Err(DownloadError::InvalidPlan(format!(
            "options.chunkSize {} is outside {MIN_CHUNK_SIZE}..={MAX_CHUNK_SIZE} (0 disables ranges)",
            plan.options.chunk_size
        )));
    }
    if let Some(proxy) = &plan.options.proxy {
        let url = Url::parse(proxy.url.trim())
            .map_err(|err| DownloadError::InvalidPlan(format!("proxy url is invalid: {err}")))?;
        if !matches!(
            url.scheme(),
            "http" | "https" | "socks4" | "socks4a" | "socks5" | "socks5h"
        ) || url.host_str().is_none()
        {
            return Err(DownloadError::InvalidPlan(
                "proxy url must be http(s)/socks with a host".into(),
            ));
        }
        if proxy.no_proxy.len() > 256 {
            return Err(DownloadError::InvalidPlan(
                "options.proxy.noProxy has too many entries".into(),
            ));
        }
        for entry in &proxy.no_proxy {
            if entry.len() > 255 {
                return Err(DownloadError::InvalidPlan(
                    "options.proxy.noProxy entry is too long".into(),
                ));
            }
        }
    }

    if plan.files.len() > MAX_FILES {
        return Err(DownloadError::PlanTooLarge(format!(
            "{} files exceeds limit {MAX_FILES}",
            plan.files.len()
        )));
    }

    let root_input = Path::new(&plan.root);
    if !root_input.is_absolute() {
        return Err(DownloadError::InvalidPlan(format!(
            "root must be an absolute path: {}",
            plan.root
        )));
    }
    let root = std::fs::canonicalize(root_input).map_err(|err| {
        DownloadError::InvalidPlan(format!("root cannot be resolved ({}): {err}", plan.root))
    })?;
    let root_meta = std::fs::metadata(&root)
        .map_err(|err| DownloadError::InvalidPlan(format!("root is not accessible: {err}")))?;
    if !root_meta.is_dir() {
        return Err(DownloadError::InvalidPlan("root is not a directory".into()));
    }

    let mut seen_destinations = HashSet::with_capacity(plan.files.len());
    let mut files = Vec::with_capacity(plan.files.len());
    let mut total_bytes = 0_u64;

    for (index, file) in plan.files.iter().enumerate() {
        let (algorithm, hash_hex) = resolve_hash(file)?;
        if file.size > MAX_FILE_BYTES {
            return Err(DownloadError::PlanTooLarge(format!(
                "{}: size {} exceeds 1 TiB",
                file.dest, file.size
            )));
        }
        total_bytes = total_bytes
            .checked_add(file.size)
            .ok_or_else(|| DownloadError::PlanTooLarge("sum of file sizes overflows u64".into()))?;

        let dest = normalize_dest(&file.dest)?;
        let destination_key = destination_key(&dest);
        if !seen_destinations.insert(destination_key) {
            return Err(DownloadError::DuplicateDest(file.dest.clone()));
        }
        let dest_path = root.join(Path::new(&dest));
        if !dest_path.starts_with(&root) {
            return Err(DownloadError::PathEscape(file.dest.clone()));
        }
        reject_symlinked_parents(&root, &dest)?;

        if matches!(file.mode, FileMode::Link | FileMode::Symlink) {
            return Err(DownloadError::InvalidPlan(format!(
                "{}: mode '{}' is not supported by this executor build",
                file.dest,
                mode_name(file.mode)
            )));
        }

        let url = validate_url(&file.url)?;
        let mut mirrors = Vec::with_capacity(file.mirrors.len());
        let mut seen_urls = HashSet::with_capacity(file.mirrors.len() + 1);
        seen_urls.insert(url.as_str().to_owned());
        for mirror in &file.mirrors {
            let mirror_url = validate_url(mirror)?;
            if seen_urls.insert(mirror_url.as_str().to_owned()) {
                mirrors.push(mirror_url);
            }
        }

        let headers = validate_headers(&file.dest, &file.headers)?;

        files.push(ValidatedFile {
            index,
            url,
            mirrors,
            algorithm,
            hash_hex,
            size: file.size,
            dest,
            dest_path,
            mode: file.mode,
            priority: file.priority,
            headers,
        });
    }

    // Stable sort preserves producer order for equal priority.
    files.sort_by_key(|file| file.priority);

    Ok(ValidatedPlan {
        plan_id: plan.plan_id,
        root,
        concurrency: plan.concurrency,
        bandwidth_limit: plan.bandwidth_limit,
        total_bytes,
        options: plan.options,
        files,
    })
}

fn resolve_hash(file: &FileSpec) -> Result<(HashAlgorithm, String), DownloadError> {
    let spec = if let Some(spec) = &file.hash {
        Some(spec.clone())
    } else if let Some(hex) = &file.sha512 {
        Some(HashSpec {
            algo: "sha512".into(),
            hex: hex.clone(),
        })
    } else if let Some(hex) = &file.sha256 {
        Some(HashSpec {
            algo: "sha256".into(),
            hex: hex.clone(),
        })
    } else {
        file.sha1.as_ref().map(|hex| HashSpec {
            algo: "sha1".into(),
            hex: hex.clone(),
        })
    };

    let Some(spec) = spec else {
        return Err(DownloadError::InvalidHash(format!(
            "{}: hash is required",
            file.dest
        )));
    };
    let algorithm = HashAlgorithm::parse(&spec.algo).ok_or_else(|| {
        DownloadError::InvalidHash(format!(
            "{}: unsupported hash algo '{}'",
            file.dest, spec.algo
        ))
    })?;
    let hex = spec.hex.trim().to_ascii_lowercase();
    if hex.len() != algorithm.hex_len() || !hex.bytes().all(|byte| byte.is_ascii_hexdigit()) {
        return Err(DownloadError::InvalidHash(format!(
            "{}: {} hash must be {} hex characters",
            file.dest,
            algorithm.name(),
            algorithm.hex_len()
        )));
    }
    Ok((algorithm, hex))
}

fn normalize_dest(dest: &str) -> Result<String, DownloadError> {
    if dest.is_empty() {
        return Err(DownloadError::InvalidPlan("dest is empty".into()));
    }
    if dest.len() > MAX_DEST_BYTES {
        return Err(DownloadError::InvalidPlan(format!(
            "dest is longer than {MAX_DEST_BYTES} bytes"
        )));
    }
    if dest.contains('\0') {
        return Err(DownloadError::InvalidPlan("dest contains NUL".into()));
    }
    if dest.contains('\\') {
        return Err(DownloadError::InvalidPlan(format!(
            "{dest}: backslashes are not allowed, use '/'"
        )));
    }
    if dest.starts_with('/') || Path::new(dest).is_absolute() {
        return Err(DownloadError::PathEscape(format!(
            "{dest}: dest must be relative"
        )));
    }

    let mut parts = Vec::new();
    for segment in dest.split('/') {
        if segment.is_empty() {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: empty path segment"
            )));
        }
        if segment == "." {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: '.' path segment is not allowed"
            )));
        }
        if segment == ".." {
            return Err(DownloadError::PathEscape(format!(
                "{dest}: '..' path segment is not allowed"
            )));
        }
        if segment.len() > 255 {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: path segment longer than 255 bytes"
            )));
        }
        if segment.ends_with('.') || segment.ends_with(' ') {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: segment ends with a dot or space"
            )));
        }
        if segment.contains(':') {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: ':' is not allowed (Windows ADS)"
            )));
        }
        if segment.bytes().any(|byte| byte < 0x20) {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: control characters are not allowed"
            )));
        }
        if is_windows_reserved(segment) {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: '{segment}' is a reserved name"
            )));
        }
        parts.push(segment);
    }

    let file_name = parts.last().copied().unwrap_or_default();
    let lower_name = file_name.to_ascii_lowercase();
    if lower_name.ends_with(".part")
        || lower_name.ends_with(".part.meta")
        || lower_name.ends_with(".part.meta.tmp")
        || lower_name.ends_with(".part.lock")
        || lower_name == ".part"
    {
        return Err(DownloadError::InvalidPlan(format!(
            "{dest}: dest may not target a downloader sidecar file"
        )));
    }

    // Reject platform prefixes such as `C:` or paths that components would
    // reinterpret. The caller has already rejected `/` and `\`.
    if Path::new(dest)
        .components()
        .any(|component| !matches!(component, Component::Normal(_)))
    {
        return Err(DownloadError::PathEscape(dest.to_owned()));
    }

    Ok(parts.join("/"))
}

fn is_windows_reserved(segment: &str) -> bool {
    let base = segment.split('.').next().unwrap_or(segment);
    let upper = base.to_ascii_uppercase();
    matches!(upper.as_str(), "CON" | "PRN" | "AUX" | "NUL")
        || (upper.len() == 4
            && (upper.starts_with("COM") || upper.starts_with("LPT"))
            && upper.as_bytes()[3].is_ascii_digit()
            && upper.as_bytes()[3] != b'0')
}

fn destination_key(dest: &str) -> String {
    if cfg!(any(target_os = "windows", target_os = "macos")) {
        dest.to_lowercase()
    } else {
        dest.to_owned()
    }
}

fn reject_symlinked_parents(root: &Path, dest: &str) -> Result<(), DownloadError> {
    let relative = Path::new(dest);
    let Some(parent) = relative.parent() else {
        return Ok(());
    };
    let mut current = root.to_path_buf();
    for component in parent.components() {
        let Component::Normal(part) = component else {
            return Err(DownloadError::PathEscape(dest.to_owned()));
        };
        current.push(part);
        match std::fs::symlink_metadata(&current) {
            Ok(metadata) if is_link_like(&metadata) => {
                return Err(DownloadError::PathEscape(format!(
                    "{dest}: parent '{}' is a symlink or reparse point",
                    current.display()
                )));
            }
            Ok(_) => {}
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => break,
            Err(err) => {
                return Err(DownloadError::InvalidPlan(format!(
                    "{dest}: cannot inspect parent '{}': {err}",
                    current.display()
                )));
            }
        }
    }
    Ok(())
}

/// A symlink, or on Windows any reparse point (junction/mount point).
pub(crate) fn is_link_like(metadata: &std::fs::Metadata) -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;
        metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0
            || metadata.file_type().is_symlink()
    }
    #[cfg(not(windows))]
    {
        metadata.file_type().is_symlink()
    }
}

fn validate_url(raw: &str) -> Result<Url, DownloadError> {
    if raw.len() > 8_192 {
        return Err(DownloadError::InvalidPlan("URL is too long".into()));
    }
    let url = Url::parse(raw)
        .map_err(|err| DownloadError::InvalidPlan(format!("invalid URL '{raw}': {err}")))?;
    if !url.username().is_empty() || url.password().is_some() {
        return Err(DownloadError::InvalidPlan(format!(
            "URL userinfo is not allowed: {url}"
        )));
    }
    if url.host_str().is_none() {
        return Err(DownloadError::InvalidPlan(format!(
            "URL has no host: {url}"
        )));
    }
    match url.scheme() {
        "https" => Ok(url),
        "http" if is_loopback(&url) => Ok(url),
        scheme => Err(DownloadError::UnsupportedScheme(format!(
            "{scheme} is not allowed ({url}); only HTTPS (or loopback HTTP for tests)"
        ))),
    }
}

pub(crate) fn is_loopback(url: &Url) -> bool {
    match url.host() {
        Some(Host::Ipv4(address)) => address.is_loopback(),
        Some(Host::Ipv6(address)) => address.is_loopback(),
        Some(Host::Domain(domain)) => domain.eq_ignore_ascii_case("localhost"),
        _ => false,
    }
}

fn validate_headers(
    dest: &str,
    headers: &BTreeMap<String, String>,
) -> Result<Vec<(HeaderName, HeaderValue)>, DownloadError> {
    let mut result = Vec::with_capacity(headers.len());
    for (name, value) in headers {
        if name.len() > 128 || value.len() > 8 * 1024 {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: header '{name}' is too large"
            )));
        }
        let header_name = HeaderName::from_str(name).map_err(|err| {
            DownloadError::InvalidPlan(format!("{dest}: invalid header name '{name}': {err}"))
        })?;
        if !matches!(header_name.as_str(), "authorization" | "cookie") {
            return Err(DownloadError::InvalidPlan(format!(
                "{dest}: header '{}' is not in the allowlist",
                header_name.as_str()
            )));
        }
        let header_value = HeaderValue::from_str(value).map_err(|err| {
            DownloadError::InvalidPlan(format!("{dest}: invalid header value for '{name}': {err}"))
        })?;
        result.push((header_name, header_value));
    }
    Ok(result)
}

fn mode_name(mode: FileMode) -> &'static str {
    match mode {
        FileMode::Ensure => "ensure",
        FileMode::Replace => "replace",
        FileMode::Link => "link",
        FileMode::Symlink => "symlink",
        FileMode::Skip => "skip",
    }
}

const fn default_schema_version() -> u32 {
    SCHEMA_VERSION
}

const fn default_concurrency() -> u32 {
    DEFAULT_CONCURRENCY
}

const fn default_range_threshold() -> u64 {
    DEFAULT_RANGE_THRESHOLD
}

const fn default_chunk_size() -> u64 {
    DEFAULT_CHUNK_SIZE
}

/// Ranges per file are capped by growing the chunk, not by spawning more
/// tasks. A 1 TiB file with the default 4 MiB chunk becomes ~1 GiB chunks
/// rather than 262k requests (and 262k tasks).
pub(crate) fn effective_chunk_size(configured: u64, size: u64) -> u64 {
    let by_cap = size.div_ceil(MAX_CHUNKS_PER_FILE).max(1);
    configured.max(1).max(by_cap)
}

const fn default_per_host_concurrency() -> u32 {
    DEFAULT_PER_HOST_CONCURRENCY
}

const fn default_progress_interval_ms() -> u64 {
    150
}

const fn default_true() -> bool {
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_parent_escape() {
        let error = normalize_dest("../escape").expect_err("must reject");
        assert_eq!(error.code().as_str(), "PATH_ESCAPE");
    }

    #[test]
    fn rejects_absolute_dest() {
        let error = normalize_dest("/etc/passwd").expect_err("must reject");
        assert_eq!(error.code().as_str(), "PATH_ESCAPE");
    }

    #[test]
    fn rejects_sidecar_dest() {
        assert!(normalize_dest("a/b.jar.part").is_err());
        assert!(normalize_dest("a/b.jar.part.meta").is_err());
        assert!(normalize_dest("a/b.jar.part.meta.tmp").is_err());
    }

    #[test]
    fn rejects_windows_reserved_names() {
        assert!(normalize_dest("mods/CON.jar").is_err());
        assert!(normalize_dest("mods/aux").is_err());
        assert!(normalize_dest("mods/COM1.txt").is_err());
        assert!(normalize_dest("mods/COM0.txt").is_ok());
    }

    fn minimal_plan() -> DownloadPlan {
        // Validation canonicalizes the root, so the directory must exist.
        let root = std::env::temp_dir().join("archoera-dl-plan-tests");
        let _ = std::fs::create_dir_all(&root);
        DownloadPlan {
            schema_version: SCHEMA_VERSION,
            plan_id: "test".into(),
            root: root.display().to_string(),
            concurrency: DEFAULT_CONCURRENCY,
            priority: 0,
            bandwidth_limit: 0,
            total_bytes: 0,
            options: PlanOptions::default(),
            files: Vec::new(),
            post: Vec::new(),
        }
    }

    #[test]
    fn defaults_stay_file_level_for_normal_assets() {
        let options = PlanOptions::default();
        assert_eq!(options.per_host_concurrency, 8);
        assert_eq!(options.range_threshold, DEFAULT_RANGE_THRESHOLD);
        assert_eq!(options.chunk_size, DEFAULT_CHUNK_SIZE);
        assert_eq!(DEFAULT_RANGE_THRESHOLD, 8 * 1024 * 1024);
        assert_eq!(DEFAULT_CHUNK_SIZE, 4 * 1024 * 1024);
    }

    #[test]
    fn chunk_size_bounds_are_enforced() {
        for value in [1, MIN_CHUNK_SIZE - 1, MAX_CHUNK_SIZE + 1] {
            let mut plan = minimal_plan();
            plan.options.chunk_size = value;
            let error = validate_plan(&plan).expect_err("must reject");
            assert_eq!(error.code().as_str(), "INVALID_PLAN", "{value}");
        }
        for value in [0, MIN_CHUNK_SIZE, MAX_CHUNK_SIZE] {
            let mut plan = minimal_plan();
            plan.options.chunk_size = value;
            assert!(validate_plan(&plan).is_ok(), "{value}");
        }
    }

    #[test]
    fn chunk_growth_keeps_ranges_bounded() {
        assert_eq!(
            effective_chunk_size(DEFAULT_CHUNK_SIZE, 8 * 1024 * 1024),
            DEFAULT_CHUNK_SIZE
        );
        let huge = 1_u64 << 40; // 1 TiB
        let chunk = effective_chunk_size(DEFAULT_CHUNK_SIZE, huge);
        assert!(chunk >= huge.div_ceil(MAX_CHUNKS_PER_FILE));
        assert!(huge.div_ceil(chunk) <= MAX_CHUNKS_PER_FILE);
    }

    #[test]
    fn accepts_per_host_caps_and_http_proxies() {
        let mut plan = minimal_plan();
        plan.options.per_host_concurrency = 0;
        plan.options.proxy = Some(ProxySpec {
            url: "http://user:secret@127.0.0.1:7890".into(),
            no_proxy: vec!["localhost".into(), ".lan".into()],
        });
        let outcome = validate_plan(&plan);
        assert!(outcome.is_ok(), "{outcome:?}");
    }

    #[test]
    fn rejects_per_host_caps_above_the_limit() {
        let mut plan = minimal_plan();
        plan.options.per_host_concurrency = MAX_PER_HOST_CONCURRENCY + 1;
        let error = validate_plan(&plan).expect_err("must reject");
        assert_eq!(error.code().as_str(), "INVALID_PLAN");
    }

    #[test]
    fn accepts_socks_proxies() {
        for url in ["socks5://127.0.0.1:1080", "socks5h://127.0.0.1:1080"] {
            let mut plan = minimal_plan();
            plan.options.proxy = Some(ProxySpec {
                url: url.into(),
                no_proxy: Vec::new(),
            });
            let outcome = validate_plan(&plan);
            assert!(outcome.is_ok(), "{url}: {outcome:?}");
        }
    }

    #[test]
    fn rejects_non_http_proxies() {
        for url in ["ftp://proxy", "not a url"] {
            let mut plan = minimal_plan();
            plan.options.proxy = Some(ProxySpec {
                url: url.into(),
                no_proxy: Vec::new(),
            });
            let error = validate_plan(&plan).expect_err("must reject");
            assert_eq!(error.code().as_str(), "INVALID_PLAN");
        }
    }

    #[test]
    fn accepts_normal_dest() {
        assert_eq!(
            normalize_dest("versions/1.21.4/1.21.4.jar").expect("valid"),
            "versions/1.21.4/1.21.4.jar"
        );
    }
}
