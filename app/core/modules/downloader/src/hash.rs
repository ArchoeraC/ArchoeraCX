//! Streaming hash helpers. MD5/SHA-1/SHA-256/SHA-512 are accepted; MD5 is
//! only used for legacy platform manifests that publish no stronger digest.

use std::fmt::Write as _;

use sha2::Digest;
use tokio::io::AsyncReadExt;

use crate::error::DownloadError;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum HashAlgorithm {
    None,
    Md5,
    Sha1,
    Sha256,
    Sha512,
}

impl HashAlgorithm {
    pub(crate) fn parse(value: &str) -> Option<Self> {
        match value.to_ascii_lowercase().as_str() {
            "none" => Some(Self::None),
            "md5" => Some(Self::Md5),
            "sha1" | "sha-1" => Some(Self::Sha1),
            "sha256" | "sha-256" => Some(Self::Sha256),
            "sha512" | "sha-512" => Some(Self::Sha512),
            _ => None,
        }
    }

    pub(crate) const fn name(self) -> &'static str {
        match self {
            Self::None => "none",
            Self::Md5 => "md5",
            Self::Sha1 => "sha1",
            Self::Sha256 => "sha256",
            Self::Sha512 => "sha512",
        }
    }

    pub(crate) const fn hex_len(self) -> usize {
        match self {
            Self::None => 0,
            Self::Md5 => 32,
            Self::Sha1 => 40,
            Self::Sha256 => 64,
            Self::Sha512 => 128,
        }
    }

    pub(crate) fn hasher(self) -> Hasher {
        Hasher {
            inner: match self {
                Self::None => Inner::None,
                Self::Md5 => Inner::Md5(md5::Md5::new()),
                Self::Sha1 => Inner::Sha1(sha1::Sha1::new()),
                Self::Sha256 => Inner::Sha256(sha2::Sha256::new()),
                Self::Sha512 => Inner::Sha512(sha2::Sha512::new()),
            },
        }
    }
}

enum Inner {
    None,
    Md5(md5::Md5),
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
            Inner::None => {}
            Inner::Md5(hasher) => hasher.update(bytes),
            Inner::Sha1(hasher) => hasher.update(bytes),
            Inner::Sha256(hasher) => hasher.update(bytes),
            Inner::Sha512(hasher) => hasher.update(bytes),
        }
    }

    pub(crate) fn finalize_hex(self) -> String {
        let bytes = match self.inner {
            Inner::None => Vec::new(),
            Inner::Md5(hasher) => hasher.finalize().to_vec(),
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
    if algorithm == HashAlgorithm::None {
        // The caller explicitly accepted a provider without a digest; the
        // archive is still size-checked and its ZIP CRCs are verified later.
        // Touch the file so a missing destination is not treated as verified.
        let _ = tokio::fs::metadata(path).await?;
        return Ok(String::new());
    }
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
    fn none_hashes_produce_an_empty_digest() {
        assert_eq!(HashAlgorithm::parse("none"), Some(HashAlgorithm::None));
        assert_eq!(HashAlgorithm::None.hex_len(), 0);
        assert_eq!(HashAlgorithm::None.hasher().finalize_hex(), "");
    }

    #[test]
    fn sha1_of_empty_input() {
        assert_eq!(
            HashAlgorithm::Sha1.hasher().finalize_hex(),
            "da39a3ee5e6b4b0d3255bfef95601890afd80709"
        );
    }

    #[test]
    fn md5_of_empty_input_for_legacy_platforms() {
        assert_eq!(
            HashAlgorithm::Md5.hasher().finalize_hex(),
            "d41d8cd98f00b204e9800998ecf8427e"
        );
    }
}
