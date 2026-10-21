// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 纯 Rust 加密原语封装（RFC 9106 Argon2id / HKDF-SHA256 / HMAC-SHA256 /
//! AES-256-GCM）——与 C# BCL 实现语义逐字节对齐。

use aes_gcm::aead::{Aead, Payload};
use aes_gcm::{Aes256Gcm, KeyInit, Nonce};
use argon2::{Algorithm, Argon2, AssociatedData, ParamsBuilder, Version};
use hkdf::Hkdf;
use hmac::{Hmac, Mac};
use sha2::Sha256;

use crate::errors::{Result, VaultError};

/// KDF 参数（随 vault 文件头/槽位存储）。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct KdfParams {
    pub m_kib: u32,
    pub t: u32,
    pub p: u32,
}

impl KdfParams {
    pub const DEFAULT: KdfParams = KdfParams {
        m_kib: 65536,
        t: 3,
        p: 1,
    };
}

/// 自检：RFC 9106 §5.3 Argon2id 向量 + 本地参考实现交叉验证向量（与 C# SelfTest 一致）。
pub fn selftest() -> bool {
    let cases: &[(&[u8], &[u8], &[u8], &[u8], u32, u32, u32, &str)] = &[
        (
            b"password",
            b"somesalt",
            b"",
            b"",
            32,
            3,
            4,
            "bb0cc80a3e671149526915418c6eefe761bb19d5d2d567a017703e0cea6ab05c",
        ),
        (
            b"password",
            b"somesalt",
            b"",
            b"",
            65536,
            3,
            4,
            "661fefbd6f29bcbc8f4646abc32a9d7a4645bb5c059537f8a5587f31adbecccd",
        ),
        (
            b"testpass",
            b"0123456789abcdef",
            b"",
            b"",
            19456,
            2,
            1,
            "9f9478c592f2998226442b4b4aa33ce610ab8ed6dff16945ba62339e70b05cea",
        ),
        (
            &[0x01u8; 32],
            &[0x02u8; 16],
            &[0x03u8; 8],
            &[0x04u8; 12],
            32,
            3,
            4,
            "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659",
        ),
    ];
    for (p, s, k, x, m, t, pp, exp) in cases {
        let kdf = KdfParams {
            m_kib: *m,
            t: *t,
            p: *pp,
        };
        // 带 secret 的向量须用 new_with_secret 路径（此处 selftest 仅覆盖 AD 版本，
        // 与 C# 一致区分：k 非空时以 secret 派生）。
        let got = match derive_with_secret(p, s, k, x, kdf, 32) {
            Ok(g) => g,
            Err(_) => return false,
        };
        if hex(&got) != *exp {
            return false;
        }
    }
    true
}

fn derive_with_secret(
    password: &[u8],
    salt: &[u8],
    secret: &[u8],
    ad: &[u8],
    kdf: KdfParams,
    out_len: usize,
) -> Result<Vec<u8>> {
    use argon2::{Algorithm, Argon2, AssociatedData, ParamsBuilder, Version};
    let mut b = ParamsBuilder::new();
    b.m_cost(kdf.m_kib)
        .t_cost(kdf.t)
        .p_cost(kdf.p)
        .output_len(out_len);
    if !ad.is_empty() {
        b.data(AssociatedData::new(ad).map_err(|e| VaultError::invalid_data(e.to_string()))?);
    }
    let params = b
        .build()
        .map_err(|e| VaultError::invalid_data(e.to_string()))?;
    let ctx = if secret.is_empty() {
        Argon2::new(Algorithm::Argon2id, Version::V0x13, params)
    } else {
        Argon2::new_with_secret(secret, Algorithm::Argon2id, Version::V0x13, params)
            .map_err(|e| VaultError::invalid_data(e.to_string()))?
    };
    let mut out = vec![0u8; out_len];
    ctx.hash_password_into(password, salt, &mut out)
        .map_err(|e| VaultError::invalid_data(e.to_string()))?;
    Ok(out)
}

fn hex(b: &[u8]) -> String {
    b.iter().map(|x| format!("{x:02x}")).collect()
}

/// 填充 [buf] 为密码学随机字节。
pub fn random_bytes(buf: &mut [u8]) -> Result<()> {
    getrandom::getrandom(buf)
        .map_err(|e| VaultError::invalid_operation(format!("随机源不可用：{e}")))
}

pub fn random_vec(n: usize) -> Result<Vec<u8>> {
    let mut v = vec![0u8; n];
    random_bytes(&mut v)?;
    Ok(v)
}

/// Argon2id v1.3（RFC 9106）派生；`ad` 为关联数据（vaultId‖kind）。
pub fn argon2id(
    password: &[u8],
    salt: &[u8],
    ad: &[u8],
    kdf: KdfParams,
    out_len: usize,
) -> Result<Vec<u8>> {
    let mut b = ParamsBuilder::new();
    b.m_cost(kdf.m_kib)
        .t_cost(kdf.t)
        .p_cost(kdf.p)
        .output_len(out_len);
    if !ad.is_empty() {
        let a = AssociatedData::new(ad)
            .map_err(|e| VaultError::invalid_data(format!("Argon2id 关联数据非法：{e}")))?;
        b.data(a);
    }
    let params = b
        .build()
        .map_err(|e| VaultError::invalid_data(format!("KDF 参数非法：{e}")))?;
    let ctx = Argon2::new(Algorithm::Argon2id, Version::V0x13, params);
    let mut out = vec![0u8; out_len];
    ctx.hash_password_into(password, salt, &mut out)
        .map_err(|e| VaultError::invalid_data(format!("Argon2id 派生失败：{e}")))?;
    Ok(out)
}

/// HKDF-SHA256（extract-then-expand）。
pub fn hkdf_sha256(ikm: &[u8], salt: &[u8], info: &[u8], out_len: usize) -> Vec<u8> {
    let hk = Hkdf::<Sha256>::new(Some(salt), ikm);
    let mut okm = vec![0u8; out_len];
    // out_len=32 远小于 255*32，expand 不会失败。
    hk.expand(info, &mut okm).expect("HKDF expand 长度合法");
    okm
}

/// HMAC-SHA256。
pub fn hmac_sha256(key: &[u8], msg: &[u8]) -> [u8; 32] {
    let mut mac = <Hmac<Sha256> as Mac>::new_from_slice(key).expect("HMAC 接受任意长度密钥");
    mac.update(msg);
    mac.finalize().into_bytes().into()
}

/// AES-256-GCM 加密：输出 `ct‖tag`（tag 16B 附于密文尾）。
pub fn aes256gcm_encrypt(
    key: &[u8],
    nonce: &[u8],
    plaintext: &[u8],
    aad: &[u8],
) -> Result<Vec<u8>> {
    let cipher = Aes256Gcm::new_from_slice(key)
        .map_err(|_| VaultError::invalid_data("AES-256-GCM 密钥长度异常"))?;
    let n = Nonce::from_slice(nonce);
    cipher
        .encrypt(n, Payload { msg: plaintext, aad })
        .map_err(|_| VaultError::auth_tag("AES-256-GCM 加密失败"))
}

/// AES-256-GCM 解密：输入 `ct‖tag`；认证失败抛 [VaultError::AuthTag]。
pub fn aes256gcm_decrypt(
    key: &[u8],
    nonce: &[u8],
    ciphertext_and_tag: &[u8],
    aad: &[u8],
) -> Result<Vec<u8>> {
    let cipher = Aes256Gcm::new_from_slice(key)
        .map_err(|_| VaultError::invalid_data("AES-256-GCM 密钥长度异常"))?;
    let n = Nonce::from_slice(nonce);
    cipher
        .decrypt(
            n,
            Payload {
                msg: ciphertext_and_tag,
                aad,
            },
        )
        .map_err(|_| VaultError::auth_tag("AES-256-GCM 认证失败（篡改/密钥错误）"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn argon2id_rfc9106_vectors() {
        let cases: &[(&[u8], &[u8], &[u8], &[u8], u32, u32, u32, &str)] = &[
            (
                b"password",
                b"somesalt",
                b"",
                b"",
                32,
                3,
                4,
                "bb0cc80a3e671149526915418c6eefe761bb19d5d2d567a017703e0cea6ab05c",
            ),
            (
                b"testpass",
                b"0123456789abcdef",
                b"",
                b"",
                19456,
                2,
                1,
                "9f9478c592f2998226442b4b4aa33ce610ab8ed6dff16945ba62339e70b05cea",
            ),
        ];
        for (p, s, k, x, m, t, pp, exp) in cases {
            let kdf = KdfParams {
                m_kib: *m,
                t: *t,
                p: *pp,
            };
            let got = argon2id(p, s, x, kdf, 32).unwrap();
            assert_eq!(hex(&got), *exp, "m={m} t={t} p={pp}");
            let _ = k;
        }
    }

    fn hex(b: &[u8]) -> String {
        b.iter().map(|x| format!("{x:02x}")).collect()
    }
}
