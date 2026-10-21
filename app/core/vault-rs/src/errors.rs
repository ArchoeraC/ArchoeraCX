// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 错误类型：镜像 C# 的异常分层（类型名用于 `err <TypeName>: <message>`，
//! `SecretStoreUnavailable` 为唯一不携带类型名的特例，与 Program.cs 顶层 catch 一致）。

#[derive(Debug)]
pub enum VaultError {
    InvalidOperation(String),
    InvalidData(String),
    Argument(String),
    /// AES-GCM 认证失败（口令/份额错误、文件篡改）。
    AuthTag(String),
    /// OS 安全存储不可用（Linux 无 D-Bus 等）——顶层单独 catch。
    /// Windows 上 DPAPI/Keychain 路径不构造此变体（仅 Unix 的 libsecret 桩使用），
    /// 故在该目标允许未构造，避免 dead_code 噪声。
    #[cfg_attr(windows, allow(dead_code))]
    SecretStoreUnavailable(String),
    /// 设备因子缺失/损坏（环境/恢复场景，非爆破向量）。
    NeedRecovery(String),
    ShareBackendMismatch { stored: String, current: String },
    ShareMissing(String),
    Lockout(i64),
    Io(String),
}

pub type Result<T> = std::result::Result<T, VaultError>;

impl VaultError {
    pub fn invalid_operation<S: Into<String>>(m: S) -> Self {
        Self::InvalidOperation(m.into())
    }
    pub fn invalid_data<S: Into<String>>(m: S) -> Self {
        Self::InvalidData(m.into())
    }
    pub fn argument<S: Into<String>>(m: S) -> Self {
        Self::Argument(m.into())
    }
    pub fn auth_tag<S: Into<String>>(m: S) -> Self {
        Self::AuthTag(m.into())
    }
    pub fn need_recovery<S: Into<String>>(m: S) -> Self {
        Self::NeedRecovery(m.into())
    }

    /// C# 异常类型名（`err <TypeName>: <message>`）。
    pub fn type_name(&self) -> &'static str {
        match self {
            Self::InvalidOperation(_) => "InvalidOperationException",
            Self::InvalidData(_) => "InvalidDataException",
            Self::Argument(_) => "ArgumentException",
            Self::AuthTag(_) => "AuthenticationTagMismatchException",
            Self::SecretStoreUnavailable(_) => "SecretStoreUnavailableException",
            Self::NeedRecovery(_) => "NeedRecoveryException",
            Self::ShareBackendMismatch { .. } => "VaultShareBackendMismatchException",
            Self::ShareMissing(_) => "VaultShareMissingException",
            Self::Lockout(_) => "LockoutException",
            Self::Io(_) => "IOException",
        }
    }

    /// 面向用户的错误消息（不含明文/密文片段）。
    pub fn message(&self) -> String {
        match self {
            Self::InvalidOperation(m)
            | Self::InvalidData(m)
            | Self::Argument(m)
            | Self::AuthTag(m)
            | Self::SecretStoreUnavailable(m)
            | Self::NeedRecovery(m)
            | Self::ShareMissing(m)
            | Self::Io(m) => m.clone(),
            Self::ShareBackendMismatch { stored, current } => format!(
                "份额存储后端不配对：vault 记录 [{stored}]，当前为 [{current}]——\
                 不同构建形态混用同一数据目录所致，请销毁重建 vault"
            ),
            Self::Lockout(secs) => format!("解锁失败过多，已临时锁定 {secs} 秒后重试"),
        }
    }
}

impl From<std::io::Error> for VaultError {
    fn from(e: std::io::Error) -> Self {
        Self::Io(e.to_string())
    }
}
