//! HTTP client construction: conservative timeouts, redirect rules and
//! explicit-only proxies.
//!
//! Two clients share every setting and differ only in protocol negotiation:
//! `auto` advertises `h2` and `http/1.1` through ALPN (small files, single
//! streams), `http1` pins HTTP/1.1 so every ranged chunk owns a TCP
//! connection (see `protocol.rs` and docs/08 §13).

use std::net::SocketAddr;
use std::time::Duration;

use reqwest::redirect::Policy;
use url::{Host, Url};

use crate::error::DownloadError;
use crate::plan::{PlanOptions, is_loopback};

pub(crate) const USER_AGENT: &str = concat!(
    "ArchoeraCX/",
    env!("CARGO_PKG_VERSION"),
    " (+https://archoera.invalid; contact)"
);
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);
/// Between body bytes; also enforced per chunk in the streamed write loop
/// (see `wire::stream_body`).
pub(crate) const READ_IDLE_TIMEOUT: Duration = Duration::from_secs(30);

/// The two HTTP clients every download attempt can pick from.
pub(crate) struct Clients {
    /// ALPN-negotiated client: HTTP/2 when the origin offers it.
    pub(crate) auto: reqwest::Client,
    /// HTTP/1.1-only client for ranged chunks.
    pub(crate) http1: reqwest::Client,
}

pub(crate) async fn build_clients(
    options: &PlanOptions,
    edge_sample: Option<&Url>,
) -> Result<Clients, DownloadError> {
    // The edge probe is one process-wide async result; run it once and feed
    // both clients so they share the same address ordering.
    let edge_addrs: Vec<SocketAddr> = match (&options.proxy, edge_sample) {
        (None, Some(sample)) => crate::edge::modrinth_edges(sample).await,
        _ => Vec::new(),
    };

    let auto = base_builder(options, &edge_addrs)?
        // Keep an idle HTTP/2 connection alive across the gaps between small
        // files; without this a NAT/firewall can silently drop it.
        .http2_keep_alive_interval(Duration::from_secs(20))
        .http2_keep_alive_timeout(Duration::from_secs(10))
        .http2_keep_alive_while_idle(true)
        .build()
        .map_err(|err| {
            DownloadError::Internal(format!("HTTP client build failed (ALPN): {err}"))
        })?;

    let http1 = base_builder(options, &edge_addrs)?
        .http1_only()
        .build()
        .map_err(|err| {
            DownloadError::Internal(format!("HTTP client build failed (HTTP/1.1): {err}"))
        })?;

    Ok(Clients { auto, http1 })
}

fn base_builder(
    options: &PlanOptions,
    edge_addrs: &[SocketAddr],
) -> Result<reqwest::ClientBuilder, DownloadError> {
    let mut builder = reqwest::Client::builder()
        .user_agent(USER_AGENT)
        .connect_timeout(CONNECT_TIMEOUT)
        .read_timeout(READ_IDLE_TIMEOUT)
        .tcp_keepalive(Duration::from_secs(15))
        .tcp_nodelay(true)
        .pool_idle_timeout(Duration::from_secs(30))
        .pool_max_idle_per_host(8)
        .redirect(Policy::custom(|attempt| {
            let too_many = attempt.previous().len() >= 5;
            let origin = attempt.previous().first().cloned();
            let allowed = is_redirect_allowed(attempt.url(), origin.as_ref());
            if too_many {
                attempt.error(std::io::Error::other("too many redirects"))
            } else if allowed {
                attempt.follow()
            } else {
                attempt.error(std::io::Error::other("redirect scheme rejected"))
            }
        }));

    // The proxy is resolved by the host process (C#) and handed over
    // explicitly; the executor never guesses one on its own. Without a plan
    // proxy, reqwest would still pick up the *_PROXY environment variables,
    // so they are disabled here: an unconfigured plan must download directly.
    match &options.proxy {
        Some(proxy) => {
            let mut configured = reqwest::Proxy::all(proxy.url.trim()).map_err(|err| {
                DownloadError::InvalidPlan(format!("proxy url is invalid: {err}"))
            })?;
            if !proxy.no_proxy.is_empty() {
                if let Some(no_proxy) = reqwest::NoProxy::from_string(&proxy.no_proxy.join(",")) {
                    configured = configured.no_proxy(Some(no_proxy));
                }
            }
            builder = builder.proxy(configured);
        }
        None => {
            builder = builder.no_proxy();
            if !edge_addrs.is_empty() {
                // No proxy: pick the Fastly edge that actually delivers on this
                // network instead of trusting one DNS answer (see edge.rs).
                builder = builder.resolve_to_addrs(crate::edge::EDGE_HOST, edge_addrs);
            }
        }
    }

    Ok(builder)
}

fn is_redirect_allowed(url: &Url, origin: Option<&Url>) -> bool {
    if !url.username().is_empty() || url.password().is_some() {
        return false;
    }
    match url.scheme() {
        "https" => {
            // A public origin must not be able to redirect the executor onto a
            // loopback/LAN/metadata address (SSRF). A plan that already starts
            // on a private origin may stay there, which keeps LAN mirrors.
            let origin_private = origin.is_some_and(is_private_target);
            !is_private_target(url) || origin_private
        }
        // Plaintext is a test-only loopback path. A public origin must not be
        // able to redirect the executor onto a local HTTP service.
        "http" => {
            is_loopback(url)
                && origin.is_some_and(|origin| origin.scheme() == "http" && is_loopback(origin))
        }
        _ => false,
    }
}

/// IP literals that point inside the host or the local network. DNS names are
/// not resolved here, so a rebinding hostname is outside this check.
fn is_private_target(url: &Url) -> bool {
    match url.host() {
        Some(Host::Ipv4(ip)) => is_private_v4(ip),
        Some(Host::Ipv6(ip)) => {
            if ip.is_loopback() || ip.is_unspecified() {
                return true;
            }
            if let Some(mapped) = ip.to_ipv4_mapped() {
                return is_private_v4(mapped);
            }
            let first = ip.segments()[0];
            (first & 0xfe00) == 0xfc00 || (first & 0xffc0) == 0xfe80
        }
        Some(Host::Domain(name)) => {
            let name = name.trim_end_matches('.').to_ascii_lowercase();
            name == "localhost" || name.ends_with(".localhost") || name.ends_with(".local")
        }
        None => false,
    }
}

fn is_private_v4(ip: std::net::Ipv4Addr) -> bool {
    ip.is_private() || ip.is_loopback() || ip.is_link_local() || ip.is_unspecified()
}

#[cfg(test)]
mod tests {
    use super::is_redirect_allowed;
    use url::Url;

    fn url(value: &str) -> Url {
        Url::parse(value).expect("test url")
    }

    #[test]
    fn redirects_stay_on_https_except_loopback_tests() {
        let public = url("https://piston-data.mojang.com/a.jar");
        assert!(is_redirect_allowed(&public, Some(&public)));
        assert!(is_redirect_allowed(
            &url("https://cdn.example/a.jar"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("https://127.0.0.1/admin"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("https://10.0.0.5/admin"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("https://169.254.169.254/latest/meta-data"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("https://printer.local/admin"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("https://[::ffff:10.0.0.1]/admin"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("http://127.0.0.1:8080/admin"),
            Some(&public)
        ));
        assert!(!is_redirect_allowed(
            &url("http://localhost:8080/admin"),
            Some(&public)
        ));
        // A plan that already targets a LAN mirror may follow its redirects.
        assert!(is_redirect_allowed(
            &url("https://10.0.0.5/file"),
            Some(&url("https://10.0.0.4/mirror"))
        ));
    }

    #[test]
    fn loopback_tests_may_redirect_within_loopback() {
        let test = url("http://127.0.0.1:1234/a");
        assert!(is_redirect_allowed(&url("http://127.0.0.1:5678/b"), Some(&test)));
        assert!(!is_redirect_allowed(
            &url("http://127.0.0.1:5678/b"),
            Some(&url("http://example.com/a"))
        ));
    }

    #[test]
    fn rejects_userinfo_and_unknown_schemes() {
        assert!(!is_redirect_allowed(
            &url("https://user:pass@example.com/a"),
            Some(&url("https://example.com"))
        ));
        assert!(!is_redirect_allowed(
            &url("ftp://example.com/a"),
            Some(&url("https://example.com"))
        ));
    }
}
