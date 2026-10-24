//! Error taxonomy for `archoera_dl`.
//!
//! Numeric codes follow `docs/08-technical-research/05-download-engine.md` §9
//! and the mapping table in `docs/10-architecture-design.md` §9.8.

use std::io;

/// Numeric + symbolic error codes shared with the C++/C# hosts.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ErrorCode {
    InvalidPlan,
    PathEscape,
    InvalidHash,
    DuplicateDest,
    UnsupportedScheme,
    PlanTooLarge,
    Dns,
    Connect,
    Tls,
    Timeout,
    Http4xx,
    Http429,
    Http5xx,
    SizeMismatch,
    RedirectLoop,
    EncodingUnsupported,
    HashMismatch,
    DiskFull,
    PermissionDenied,
    FileLocked,
    PathTooLong,
    CrossDevice,
    DestIsDir,
    ReadOnly,
    Cancelled,
    Internal,
}

impl ErrorCode {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::InvalidPlan => "INVALID_PLAN",
            Self::PathEscape => "PATH_ESCAPE",
            Self::InvalidHash => "INVALID_HASH",
            Self::DuplicateDest => "DUPLICATE_DEST",
            Self::UnsupportedScheme => "UNSUPPORTED_SCHEME",
            Self::PlanTooLarge => "PLAN_TOO_LARGE",
            Self::Dns => "DNS",
            Self::Connect => "CONNECT",
            Self::Tls => "TLS",
            Self::Timeout => "TIMEOUT",
            Self::Http4xx => "HTTP_4XX",
            Self::Http429 => "HTTP_429",
            Self::Http5xx => "HTTP_5XX",
            Self::SizeMismatch => "SIZE_MISMATCH",
            Self::RedirectLoop => "REDIRECT_LOOP",
            Self::EncodingUnsupported => "ENCODING_UNSUPPORTED",
            Self::HashMismatch => "HASH_MISMATCH",
            Self::DiskFull => "DISK_FULL",
            Self::PermissionDenied => "PERMISSION_DENIED",
            Self::FileLocked => "FILE_LOCKED",
            Self::PathTooLong => "PATH_TOO_LONG",
            Self::CrossDevice => "CROSS_DEVICE",
            Self::DestIsDir => "DEST_IS_DIR",
            Self::ReadOnly => "READ_ONLY",
            Self::Cancelled => "CANCELLED",
            Self::Internal => "INTERNAL",
        }
    }

    pub const fn numeric(self) -> u32 {
        match self {
            Self::InvalidPlan => 1001,
            Self::PathEscape => 1002,
            Self::InvalidHash => 1003,
            Self::DuplicateDest => 1004,
            Self::UnsupportedScheme => 1005,
            Self::PlanTooLarge => 1006,
            Self::Dns => 2001,
            Self::Connect => 2002,
            Self::Tls => 2003,
            Self::Timeout => 2004,
            Self::Http4xx => 2006,
            Self::Http429 => 2007,
            Self::Http5xx => 2008,
            Self::SizeMismatch => 2010,
            Self::RedirectLoop => 2011,
            Self::EncodingUnsupported => 2013,
            Self::HashMismatch => 3001,
            Self::DiskFull => 4001,
            Self::PermissionDenied => 4002,
            Self::FileLocked => 4003,
            Self::PathTooLong => 4004,
            Self::CrossDevice => 4005,
            Self::DestIsDir => 4006,
            Self::ReadOnly => 4007,
            Self::Cancelled => 5001,
            Self::Internal => 9001,
        }
    }

    /// Whether trying the same source again (with backoff) is worthwhile.
    pub const fn retryable(self) -> bool {
        matches!(
            self,
            Self::Dns
                | Self::Connect
                | Self::Tls
                | Self::Timeout
                | Self::Http429
                | Self::Http5xx
                | Self::SizeMismatch
                | Self::FileLocked
        )
    }
}

impl std::fmt::Display for ErrorCode {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(self.as_str())
    }
}

/// A downloader failure. Plan validation failures and per-file failures share
/// this type; per-file failures are folded into the result JSON.
#[derive(Debug, thiserror::Error)]
pub enum DownloadError {
    #[error("invalid plan: {0}")]
    InvalidPlan(String),
    #[error("path escapes root: {0}")]
    PathEscape(String),
    #[error("invalid hash: {0}")]
    InvalidHash(String),
    #[error("duplicate destination: {0}")]
    DuplicateDest(String),
    #[error("unsupported URL scheme: {0}")]
    UnsupportedScheme(String),
    #[error("plan too large: {0}")]
    PlanTooLarge(String),

    #[error("DNS failure: {0}")]
    Dns(String),
    #[error("connect failure: {0}")]
    Connect(String),
    #[error("TLS failure: {0}")]
    Tls(String),
    #[error("timeout: {0}")]
    Timeout(String),
    #[error("HTTP {status}")]
    Http4xx { status: u16 },
    #[error("HTTP 429")]
    Http429,
    #[error("HTTP {status}")]
    Http5xx { status: u16 },
    #[error("expected {expected} bytes, got {actual}")]
    SizeMismatch { expected: u64, actual: u64 },
    #[error("redirect rejected")]
    RedirectLoop,
    #[error("unsupported content encoding: {0}")]
    EncodingUnsupported(String),

    #[error("expected {expected}, got {actual}")]
    HashMismatch { expected: String, actual: String },

    #[error("disk full: {0}")]
    DiskFull(String),
    #[error("permission denied: {0}")]
    PermissionDenied(String),
    #[error("file locked: {0}")]
    FileLocked(String),
    #[error("path too long: {0}")]
    PathTooLong(String),
    #[error("cross-device operation: {0}")]
    CrossDevice(String),
    #[error("destination is a directory: {0}")]
    DestIsDir(String),
    #[error("read-only filesystem: {0}")]
    ReadOnly(String),
    #[error("cancelled")]
    Cancelled,
    #[error("internal error: {0}")]
    Internal(String),
}

impl DownloadError {
    pub const fn code(&self) -> ErrorCode {
        match self {
            Self::InvalidPlan(_) => ErrorCode::InvalidPlan,
            Self::PathEscape(_) => ErrorCode::PathEscape,
            Self::InvalidHash(_) => ErrorCode::InvalidHash,
            Self::DuplicateDest(_) => ErrorCode::DuplicateDest,
            Self::UnsupportedScheme(_) => ErrorCode::UnsupportedScheme,
            Self::PlanTooLarge(_) => ErrorCode::PlanTooLarge,
            Self::Dns(_) => ErrorCode::Dns,
            Self::Connect(_) => ErrorCode::Connect,
            Self::Tls(_) => ErrorCode::Tls,
            Self::Timeout(_) => ErrorCode::Timeout,
            Self::Http4xx { .. } => ErrorCode::Http4xx,
            Self::Http429 => ErrorCode::Http429,
            Self::Http5xx { .. } => ErrorCode::Http5xx,
            Self::SizeMismatch { .. } => ErrorCode::SizeMismatch,
            Self::RedirectLoop => ErrorCode::RedirectLoop,
            Self::EncodingUnsupported(_) => ErrorCode::EncodingUnsupported,
            Self::HashMismatch { .. } => ErrorCode::HashMismatch,
            Self::DiskFull(_) => ErrorCode::DiskFull,
            Self::PermissionDenied(_) => ErrorCode::PermissionDenied,
            Self::FileLocked(_) => ErrorCode::FileLocked,
            Self::PathTooLong(_) => ErrorCode::PathTooLong,
            Self::CrossDevice(_) => ErrorCode::CrossDevice,
            Self::DestIsDir(_) => ErrorCode::DestIsDir,
            Self::ReadOnly(_) => ErrorCode::ReadOnly,
            Self::Cancelled => ErrorCode::Cancelled,
            Self::Internal(_) => ErrorCode::Internal,
        }
    }

    /// Local failures abort the whole file; network/HTTP/integrity failures may
    /// still be attempted against another mirror.
    pub const fn is_local_failure(&self) -> bool {
        matches!(
            self,
            Self::DiskFull(_)
                | Self::PermissionDenied(_)
                | Self::FileLocked(_)
                | Self::PathTooLong(_)
                | Self::ReadOnly(_)
                | Self::DestIsDir(_)
                | Self::PathEscape(_)
                | Self::Cancelled
        )
    }

    /// Whether a retry against the same source can plausibly succeed.
    ///
    /// A TLS handshake reset or timeout is transient on cross-border links and
    /// must be retried; a certificate/hostname verification failure is not,
    /// because the same certificate will be presented again.
    pub fn retryable(&self) -> bool {
        if let Self::Tls(message) = self {
            return !looks_like_certificate_failure(message);
        }
        self.code().retryable()
    }

    pub fn from_reqwest(err: reqwest::Error) -> Self {
        if err.is_timeout() {
            return Self::Timeout(err.to_string());
        }
        if err.is_redirect() {
            return Self::RedirectLoop;
        }
        if err.is_builder() || err.is_decode() {
            return Self::Internal(err.to_string());
        }
        let text = err.to_string();
        let lower = text.to_ascii_lowercase();
        if lower.contains("certificate") || lower.contains("tls") || lower.contains("ssl") {
            return Self::Tls(text);
        }
        if lower.contains("dns") || lower.contains("name or service") {
            return Self::Dns(text);
        }
        Self::Connect(text)
    }

    /// Map an OS error to the error taxonomy. `raw_os_error` values are
    /// platform-specific (Linux 39 is `ENOTEMPTY`, Windows 39 is
    /// `ERROR_HANDLE_EOF`), so the tables must not be shared across systems.
    pub fn from_io(err: io::Error) -> Self {
        let message = err.to_string();

        #[cfg(target_os = "linux")]
        {
            match err.raw_os_error() {
                Some(28) | Some(122) => return Self::DiskFull(message), // ENOSPC, EDQUOT
                Some(1) | Some(13) => return Self::PermissionDenied(message), // EPERM, EACCES
                Some(16) => return Self::FileLocked(message),               // EBUSY
                Some(18) => return Self::CrossDevice(message),              // EXDEV
                Some(21) => return Self::DestIsDir(message),                // EISDIR
                Some(30) => return Self::ReadOnly(message),                 // EROFS
                Some(36) => return Self::PathTooLong(message),              // ENAMETOOLONG
                _ => {}
            }
        }

        #[cfg(target_os = "macos")]
        {
            match err.raw_os_error() {
                Some(28) | Some(69) => return Self::DiskFull(message), // ENOSPC, EDQUOT
                Some(1) | Some(13) => return Self::PermissionDenied(message),
                Some(16) => return Self::FileLocked(message),
                Some(18) => return Self::CrossDevice(message),
                Some(21) => return Self::DestIsDir(message),
                Some(30) => return Self::ReadOnly(message),
                Some(63) => return Self::PathTooLong(message), // ENAMETOOLONG
                _ => {}
            }
        }

        #[cfg(windows)]
        {
            match err.raw_os_error() {
                Some(112) => return Self::DiskFull(message),       // ERROR_DISK_FULL
                Some(5) => return Self::PermissionDenied(message), // ERROR_ACCESS_DENIED
                Some(32) | Some(33) => return Self::FileLocked(message), // sharing/lock
                Some(17) => return Self::CrossDevice(message),     // ERROR_NOT_SAME_DEVICE
                Some(19) => return Self::ReadOnly(message),        // ERROR_WRITE_PROTECT
                Some(206) => return Self::PathTooLong(message),    // ERROR_FILENAME_EXCED_RANGE
                Some(267) => return Self::DestIsDir(message),      // ERROR_DIRECTORY
                _ => {}
            }
        }

        if err.kind() == io::ErrorKind::PermissionDenied {
            return Self::PermissionDenied(message);
        }
        Self::Internal(message)
    }
}

impl From<io::Error> for DownloadError {
    fn from(err: io::Error) -> Self {
        Self::from_io(err)
    }
}

impl From<reqwest::Error> for DownloadError {
    fn from(err: reqwest::Error) -> Self {
        Self::from_reqwest(err)
    }
}

impl From<serde_json::Error> for DownloadError {
    fn from(err: serde_json::Error) -> Self {
        Self::InvalidPlan(format!("JSON parse failed: {err}"))
    }
}

fn looks_like_certificate_failure(message: &str) -> bool {
    let lower = message.to_ascii_lowercase();
    [
        "certificate",
        "unknown issuer",
        "self signed",
        "self-signed",
        "invalid peer",
        "hostname mismatch",
        "not valid for",
    ]
    .iter()
    .any(|needle| lower.contains(needle))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn codes_are_stable() {
        assert_eq!(ErrorCode::InvalidPlan.numeric(), 1001);
        assert_eq!(ErrorCode::HashMismatch.as_str(), "HASH_MISMATCH");
        assert_eq!(ErrorCode::Cancelled.numeric(), 5001);
        assert!(ErrorCode::Http5xx.retryable());
        // A TLS handshake reset is transient and must be retried.
        assert!(ErrorCode::Tls.retryable());
    }

    #[test]
    fn tls_resets_retry_but_certificate_failures_do_not() {
        assert!(DownloadError::Tls("unexpected eof while reading".into()).retryable());
        assert!(!DownloadError::Tls("certificate verify failed".into()).retryable());
        assert!(!DownloadError::Tls("invalid peer certificate".into()).retryable());
    }

    #[test]
    fn error_code_mapping_for_local_failure() {
        assert_eq!(ErrorCode::DiskFull.as_str(), "DISK_FULL");
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn linux_errno_mapping_is_platform_specific() {
        use std::io;

        let code = |errno| {
            DownloadError::from_io(io::Error::from_raw_os_error(errno))
                .code()
                .as_str()
        };
        assert_eq!(code(28), "DISK_FULL"); // ENOSPC
        assert_eq!(code(122), "DISK_FULL"); // EDQUOT
        assert_eq!(code(39), "INTERNAL"); // ENOTEMPTY, must not be DISK_FULL
        assert_eq!(code(36), "PATH_TOO_LONG"); // ENAMETOOLONG
        assert_eq!(code(21), "DEST_IS_DIR"); // EISDIR
        assert_eq!(code(13), "PERMISSION_DENIED"); // EACCES
    }

    #[cfg(windows)]
    #[test]
    fn windows_error_mapping_is_platform_specific() {
        use std::io;

        let code = |errno| {
            DownloadError::from_io(io::Error::from_raw_os_error(errno))
                .code()
                .as_str()
        };
        assert_eq!(code(112), "DISK_FULL"); // ERROR_DISK_FULL
        assert_eq!(code(32), "FILE_LOCKED"); // ERROR_SHARING_VIOLATION
        assert_eq!(code(206), "PATH_TOO_LONG"); // ERROR_FILENAME_EXCED_RANGE
        assert_eq!(code(5), "PERMISSION_DENIED"); // ERROR_ACCESS_DENIED
    }
}
