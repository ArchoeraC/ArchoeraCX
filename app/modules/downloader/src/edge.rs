//! CDN edge selection.
//!
//! Fastly answers `cdn.modrinth.com` from anycast addresses, but the route to
//! each edge differs per ISP: on some networks one edge delivers megabytes per
//! second while another stalls at tens of kilobytes (or does not connect at
//! all). DNS only returns one address, and it is not always the good one, so a
//! launcher that trusts the resolver looks "throttled" while a browser — whose
//! own DNS/cache landed on a healthy edge — downloads at full speed.
//!
//! We therefore probe a handful of known Fastly anycast addresses plus the
//! resolver's answers with a small ranged GET, then hand the fastest addresses
//! to the HTTP client in order. This is a pure optimization: when every probe
//! fails the plan keeps the resolver's list, exactly as before.

use std::collections::HashSet;
use std::net::{IpAddr, SocketAddr};
use std::time::Duration;

use reqwest::header::RANGE;
use tokio::net::lookup_host;
use tokio::sync::OnceCell;
use tokio::task::JoinSet;
use url::Url;

use crate::client::USER_AGENT;

/// Host whose edges we optimize; everything else keeps plain DNS.
pub(crate) const EDGE_HOST: &str = "cdn.modrinth.com";

/// Fastly's long-lived anycast addresses. Fastly routes TLS by SNI, so the
/// certificate and `Host` header still identify `cdn.modrinth.com`; the probe
/// below drops any address that is slow or unreachable on this network.
const FASTLY_ANYCAST: [&str; 4] = [
    "151.101.1.242",
    "151.101.65.242",
    "151.101.129.242",
    "151.101.193.242",
];

const PROBE_BYTES: u64 = 128 * 1024;
const PROBE_BUDGET: Duration = Duration::from_millis(1500);
const PROBE_CONNECT: Duration = Duration::from_millis(900);

/// Process-wide result so the probe runs once per launch, not per plan.
static EDGES: OnceCell<Vec<SocketAddr>> = OnceCell::const_new();

/// Ordered addresses for `EDGE_HOST`, fastest first. Empty means "use DNS".
pub(crate) async fn modrinth_edges(sample: &Url) -> Vec<SocketAddr> {
    if sample.host_str() != Some(EDGE_HOST) || sample.scheme() != "https" {
        return Vec::new();
    }
    if std::env::var_os("ARCHOERACX_DL_NO_EDGE").is_some() {
        return Vec::new();
    }

    EDGES
        .get_or_init(|| async { probe(sample.clone()).await })
        .await
        .clone()
}

async fn probe(sample: Url) -> Vec<SocketAddr> {
    let resolved = resolve(EDGE_HOST).await;
    let mut candidates: Vec<SocketAddr> = FASTLY_ANYCAST
        .iter()
        .filter_map(|ip| ip.parse::<IpAddr>().ok())
        .map(|ip| SocketAddr::new(ip, 443))
        .collect();
    candidates.extend(resolved.iter().copied());

    let mut unique = HashSet::new();
    candidates.retain(|addr| unique.insert(*addr));

    let mut tasks = JoinSet::new();
    for addr in candidates.iter().copied() {
        tasks.spawn(probe_one(sample.clone(), addr));
    }

    let mut scored: Vec<(SocketAddr, usize)> = Vec::new();
    while let Some(outcome) = tasks.join_next().await {
        if let Ok((addr, bytes)) = outcome {
            scored.push((addr, bytes));
        }
    }

    let ordered = order(scored.clone(), resolved);
    if std::env::var_os("ARCHOERACX_DL_DEBUG").is_some() {
        eprintln!(
            "edge probe: scored={:?} chosen={:?}",
            scored.iter().map(|(a, b)| (a.to_string(), *b)).collect::<Vec<_>>(),
            ordered.iter().map(|a| a.to_string()).collect::<Vec<_>>()
        );
    }
    if ordered.is_empty() {
        // Nothing answered the probe: keep the resolver's behaviour.
        Vec::new()
    } else {
        ordered
    }
}

async fn resolve(host: &str) -> Vec<SocketAddr> {
    match lookup_host((host, 443)).await {
        Ok(addrs) => {
            let mut unique = HashSet::new();
            addrs.filter(|addr| unique.insert(*addr)).collect()
        }
        Err(_) => Vec::new(),
    }
}

async fn probe_one(sample: Url, addr: SocketAddr) -> (SocketAddr, usize) {
    let client = match reqwest::Client::builder()
        .user_agent(USER_AGENT)
        .http1_only()
        .connect_timeout(PROBE_CONNECT)
        .pool_max_idle_per_host(0)
        .resolve(EDGE_HOST, addr)
        .build()
    {
        Ok(client) => client,
        Err(_) => return (addr, 0),
    };

    let request = client
        .get(sample)
        .header(RANGE, format!("bytes=0-{}", PROBE_BYTES - 1));

    let bytes = match tokio::time::timeout(PROBE_BUDGET, async {
        let response = request.send().await.ok()?;
        if !response.status().is_success() {
            return None;
        }
        response.bytes().await.ok().map(|body| body.len())
    })
    .await
    {
        Ok(Some(len)) => len,
        _ => 0,
    };

    (addr, bytes)
}

/// Fastest successful probes first, then the resolver's addresses as a
/// fallback for when the fast edge dies later in the plan.
fn order(scored: Vec<(SocketAddr, usize)>, resolved: Vec<SocketAddr>) -> Vec<SocketAddr> {
    let mut ranked = scored;
    ranked.sort_by_key(|left| std::cmp::Reverse(left.1));

    let mut seen = HashSet::new();
    let mut ordered = Vec::new();
    for (addr, bytes) in ranked {
        if bytes > 0 && seen.insert(addr) {
            ordered.push(addr);
        }
    }
    for addr in resolved {
        if seen.insert(addr) {
            ordered.push(addr);
        }
    }
    ordered
}

#[cfg(test)]
mod tests {
    use super::*;

    fn addr(last: u8) -> SocketAddr {
        SocketAddr::new(IpAddr::from([10, 0, 0, last]), 443)
    }

    #[test]
    fn fastest_edges_come_first() {
        let ordered = order(
            vec![(addr(1), 100), (addr(2), 900), (addr(3), 0)],
            vec![addr(4)],
        );

        assert_eq!(ordered, vec![addr(2), addr(1), addr(4)]);
    }

    #[test]
    fn resolver_addresses_follow_and_never_repeat() {
        let ordered = order(vec![(addr(1), 10)], vec![addr(1), addr(2), addr(2)]);

        assert_eq!(ordered, vec![addr(1), addr(2)]);
    }

    #[test]
    fn no_successful_probe_means_no_override() {
        assert!(order(Vec::new(), Vec::new()).is_empty());
    }
}
