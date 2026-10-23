//! `.part` data-file plumbing shared by both download modes: sibling paths,
//! cleanup, the atomic commit rename and attempt pacing.

use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use crate::error::DownloadError;
use crate::resume::remove_resume;

/// The in-flight data file beside `dest`.
pub(crate) fn part_path(dest: &Path) -> PathBuf {
    let mut value = dest.as_os_str().to_os_string();
    value.push(".part");
    PathBuf::from(value)
}

/// Remove the data file; a missing file is fine and every other failure is
/// swallowed because abort handling already decided that a restart cannot
/// use this `.part`.
async fn cleanup_part(part: &Path) {
    match tokio::fs::remove_file(part).await {
        Ok(()) => {}
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => {}
        Err(_) => {}
    }
}

/// Remove the data file and its resume sidecar.
pub(crate) async fn cleanup_part_with_meta(part: &Path) {
    remove_resume(part).await;
    cleanup_part(part).await;
}

/// Atomically move `<dest>.part` into place, retrying file-lock errors a few
/// times (antivirus scanners and other launchers hold files on some hosts).
pub(crate) async fn rename_with_retry(part: &Path, dest: &Path) -> Result<(), DownloadError> {
    let mut delay = Duration::from_millis(100);
    let mut last_error = None;
    for attempt in 0..5 {
        match tokio::fs::rename(part, dest).await {
            Ok(()) => return Ok(()),
            Err(err) => {
                let error = DownloadError::from_io(err);
                let locked = matches!(error, DownloadError::FileLocked(_));
                last_error = Some(error);
                if !locked || attempt == 4 {
                    break;
                }
                tokio::time::sleep(delay).await;
                delay = delay.saturating_mul(2);
            }
        }
    }
    Err(last_error
        .unwrap_or_else(|| DownloadError::Internal("rename failed without an error".into())))
}

/// Expanding pause between attempts against one source, capped at 5 s.
pub(crate) fn backoff(attempt: u32) -> Duration {
    let millis = 500_u64.saturating_mul(1_u64 << attempt.min(6));
    Duration::from_millis(millis.min(5_000))
}

/// Elapsed milliseconds clamped into `u64`.
pub(crate) fn elapsed_ms(started: Instant) -> u64 {
    started.elapsed().as_millis().min(u128::from(u64::MAX)) as u64
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn part_path_is_sibling() {
        let dest = Path::new("/tmp/root/mods/a.jar");
        assert_eq!(part_path(dest), Path::new("/tmp/root/mods/a.jar.part"));
    }

    #[test]
    fn backoff_is_capped() {
        assert_eq!(backoff(0), Duration::from_millis(500));
        assert!(backoff(20) <= Duration::from_secs(5));
    }
}
