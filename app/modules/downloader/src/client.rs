//! HTTP client construction: conservative timeouts, redirect rules and
//! explicit-only proxies.

use std::time::Duration;

use reqwest::redirect::Policy;
use url::Url;

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

pub(crate) async fn build_client(
    options: &PlanOptions,
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
            if let Some(sample) = edge_sample {
                // No proxy: pick the Fastly edge that actually delivers on this
                // network instead of trusting one DNS answer (see edge.rs).
                let edges = crate::edge::modrinth_edges(sample).await;
                if !edges.is_empty() {
                    builder = builder.resolve_to_addrs(crate::edge::EDGE_HOST, &edges);
                }
            }
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
