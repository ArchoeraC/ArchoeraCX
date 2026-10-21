//! Streaming hash helpers. SHA-1/SHA-256/SHA-512 are the only accepted
//! algorithms (see `docs/10-architecture-design.md` §9.2).

use std::fmt::Write as _;

use sha2::Digest;
use tokio::io::AsyncReadExt;

use crate::error::DownloadError;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum HashAlgorithm {
    Sha1,
    Sha256,
    Sha512,
}

impl HashAlgorithm {
    pub(crate) fn parse(value: &str) -> Option<Self> {
        match value.to_ascii_lowercase().as_str() {
            "sha1" | "sha-1" => Some(Self::Sha1),
            "sha256" | "sha-256" => Some(Self::Sha256),
            "sha512" | "sha-512" => Some(Self::Sha512),
            _ => None,
        }
    }

    pub(crate) const fn name(self) -> &'static str {
        match self {
            Self::Sha1 => "sha1",
            Self::Sha256 => "sha256",
            Self::Sha512 => "sha512",
        }
    }

    pub(crate) const fn hex_len(self) -> usize {
        match self {
            Self::Sha1 => 40,
            Self::Sha256 => 64,
            Self::Sha512 => 128,
        }
    }

    pub(crate) fn hasher(self) -> Hasher {
        Hasher {
            inner: match self {
                Self::Sha1 => Inner::Sha1(sha1::Sha1::new()),
                Self::Sha256 => Inner::Sha256(sha2::Sha256::new()),
                Self::Sha512 => Inner::Sha512(sha2::Sha512::new()),
            },
        }
    }
}

enum Inner {
    Sha1(sha1::Sha1),
    Sha256(sha2::Sha256),
    Sha512(sha2::Sha512),
}

pub(crate) struct Hasher {
    inner: Inner,
}

impl Hasher {
    pub(crate) fn update(&mut self, bytes: &[u8]) {
        match &mut self.inner {
            Inner::Sha1(hasher) => hasher.update(bytes),
            Inner::Sha256(hasher) => hasher.update(bytes),
            Inner::Sha512(hasher) => hasher.update(bytes),
        }
    }

    pub(crate) fn finalize_hex(self) -> String {
        let bytes = match self.inner {
            Inner::Sha1(hasher) => hasher.finalize().to_vec(),
            Inner::Sha256(hasher) => hasher.finalize().to_vec(),
            Inner::Sha512(hasher) => hasher.finalize().to_vec(),
        };
        to_hex(&bytes)
    }
}

pub(crate) fn to_hex(bytes: &[u8]) -> String {
    let mut out = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        // Writing into a String cannot fail.
        let _ = write!(out, "{byte:02x}");
    }
    out
}

/// Hash an on-disk file without loading it into memory.
pub(crate) async fn hash_file(
    path: &std::path::Path,
    algorithm: HashAlgorithm,
) -> Result<String, DownloadError> {
    let mut file = tokio::fs::File::open(path).await?;
    let mut hasher = algorithm.hasher();
    let mut buffer = vec![0_u8; 64 * 1024];
    loop {
        let read = file.read(&mut buffer).await?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hasher.finalize_hex())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sha1_of_empty_input() {
        assert_eq!(
            HashAlgorithm::Sha1.hasher().finalize_hex(),
            "da39a3ee5e6b4b0d3255bfef95601890afd80709"
        );
    }
}
