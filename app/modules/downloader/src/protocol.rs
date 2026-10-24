//! Per-origin HTTP version policy.
//!
//! Small files use an ALPN client (`h2` when the server offers it, HTTP/1.1
//! otherwise) to save handshakes across thousands of assets. Ranged chunks
//! keep HTTP/1.1 so every chunk owns a TCP connection and no single
//! connection's flow control caps the aggregate speed.
//!
//! When an origin fails at the protocol layer (HTTP/2 connection errors,
//! `421`, or `505` on an HTTP/2 response) the origin is demoted to HTTP/1.1
//! for a cooldown. The state is in-memory only: a new process learns again,
//! which avoids pinning a host to a bad choice after a transient network
//! problem.

use std::collections::HashMap;
use std::sync::{Mutex, MutexGuard};
use std::time::{Duration, Instant};

use crate::error::DownloadError;

/// How long a demoted origin keeps using HTTP/1.1.
pub(crate) const DEMOTION: Duration = Duration::from_secs(600);

/// Which client one attempt should use.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum HttpVersion {
    /// ALPN-negotiated: HTTP/2 when the server selects it, HTTP/1.1 otherwise.
    Auto,
    /// HTTP/1.1 only, one stream per TCP connection.
    Http1,
}

/// Demotion table keyed by lowercased host name.
pub(crate) struct ProtocolState {
    demoted: Mutex<HashMap<String, Instant>>,
}

impl ProtocolState {
    pub(crate) fn new() -> Self {
        Self {
            demoted: Mutex::new(HashMap::new()),
        }
    }

    /// Version to try first for `host`.
    pub(crate) fn choose(&self, host: &str) -> HttpVersion {
        self.choose_at(host, Instant::now())
    }

    /// Stop negotiating HTTP/2 for `host` until the cooldown expires.
    pub(crate) fn demote(&self, host: &str) {
        let host = host.to_ascii_lowercase();
        self.demote_at(&host, Instant::now());
        if std::env::var_os("ARCHOERACX_DL_DEBUG").is_some() {
            eprintln!(
                "protocol: demoting {host} to HTTP/1.1 for {}s",
                DEMOTION.as_secs()
            );
        }
    }

    fn choose_at(&self, host: &str, now: Instant) -> HttpVersion {
        let host = host.to_ascii_lowercase();
        let mut demoted = lock(&self.demoted);
        if let Some(until) = demoted.get(&host).copied() {
            if until > now {
                return HttpVersion::Http1;
            }
            // Cooldown elapsed: the next attempt is a half-open probe on Auto.
            demoted.remove(&host);
        }
        HttpVersion::Auto
    }

    fn demote_at(&self, host: &str, now: Instant) {
        lock(&self.demoted).insert(host.to_owned(), now + DEMOTION);
    }
}

/// Whether `error` says "this HTTP version is the problem", not the content.
///
/// `421`/`505` are checked by [`crate::wire::protocol_status`] with the
/// response version before they reach here; the connection-level branch is
/// best-effort because reqwest flattens hyper's error kinds into a message.
pub(crate) fn is_protocol_failure(error: &DownloadError) -> bool {
    match error {
        DownloadError::Http4xx { status: 421 } => true,
        DownloadError::Http5xx { status: 505 } => true,
        DownloadError::Connect(message) => looks_like_http2_failure(message),
        _ => false,
    }
}

fn looks_like_http2_failure(message: &str) -> bool {
    let lower = message.to_ascii_lowercase();
    [
        "http2",
        "http/2",
        "goaway",
        "stream error",
        "frame size",
        "protocol error",
    ]
    .iter()
    .any(|needle| lower.contains(needle))
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn auto_by_default_demoted_until_cooldown_expires() {
        let state = ProtocolState::new();
        let now = Instant::now();
        assert_eq!(state.choose_at("cdn.example", now), HttpVersion::Auto);

        state.demote_at("cdn.example", now);
        assert_eq!(
            state.choose_at("CDN.EXAMPLE", now + Duration::from_secs(1)),
            HttpVersion::Http1
        );
        // Another host keeps negotiating.
        assert_eq!(state.choose_at("other.example", now), HttpVersion::Auto);
        // After the cooldown the host gets one Auto attempt again.
        assert_eq!(
            state.choose_at("cdn.example", now + DEMOTION + Duration::from_secs(1)),
            HttpVersion::Auto
        );
    }

    #[test]
    fn protocol_statuses_degrade_but_content_failures_do_not() {
        assert!(is_protocol_failure(&DownloadError::Http4xx { status: 421 }));
        assert!(is_protocol_failure(&DownloadError::Http5xx { status: 505 }));
        assert!(is_protocol_failure(&DownloadError::Connect(
            "http2 error: connection error received: GOAWAY".into()
        )));
        assert!(is_protocol_failure(&DownloadError::Connect(
            "stream error received: refused stream".into()
        )));

        assert!(!is_protocol_failure(&DownloadError::Http4xx {
            status: 404
        }));
        assert!(!is_protocol_failure(&DownloadError::Http4xx {
            status: 403
        }));
        assert!(!is_protocol_failure(&DownloadError::Http429));
        assert!(!is_protocol_failure(&DownloadError::Http5xx {
            status: 500
        }));
        assert!(!is_protocol_failure(&DownloadError::Connect(
            "connection reset by peer".into()
        )));
        assert!(!is_protocol_failure(&DownloadError::Tls(
            "certificate verify failed".into()
        )));
        assert!(!is_protocol_failure(&DownloadError::HashMismatch {
            expected: "sha1:a".into(),
            actual: "b".into(),
        }));
    }
}
