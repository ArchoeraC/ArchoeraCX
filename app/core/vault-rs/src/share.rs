// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 授权侧份额（S）承载 + 解锁退避状态（[ShareBundle]）+ 后端工厂。
//!
//! 生产后端：
//!   - Linux：[LibsecretStore]（Secret Service / `org.freedesktop.Secret`，见
//!     `src/secret_service.rs`；schema `archoera.vault` + 数据目录作用域 account）或
//!     [FileStore]（`<dataDir>/secret.key`，0600，headless/显式降级）或 [NullSecretStore]
//!     （slots 自包含）。**无 D-Bus / 钥匙串锁定时明确失败，不静默降级**。
//!   - Windows：[DpapiStore]（`CryptProtectData` 密文落 `<dataDir>/sshare.bin`，
//!     指纹 `dpapi`）——与 C# `DpapiStore` 语义一致（单槽、忽略 key）。
//!   - macOS：[KeychainStore]（`kSecClassGenericPassword`，service
//!     `archoera.vault`，account 按数据目录作用域，原始字节值）——与 C#
//!     `KeychainStore` 语义一致。
//!
//! 平台后端实现见下方 `dpapi` / `keychain` 模块，全部经 `#[cfg]` 门控。

use std::path::{Path, PathBuf};

use crate::errors::{Result, VaultError};
use crate::format::KEY_SIZE;

/// 系统授权侧份额（S）的存取抽象。
pub trait SecretStore {
    fn backend(&self) -> &str;
    fn load(&self, key: &str) -> Result<Option<Vec<u8>>>;
    fn store(&self, key: &str, value: &[u8]) -> Result<()>;
    fn delete(&self, key: &str) -> Result<()>;
}

// ── ShareBundle ────────────────────────────────────────────────────

/// 份额 + 退避状态的同槽承载。布局（小端）：
/// `magic(4)="ASTB" | version(1)=1 | shareLen(1)=32 | share(32) | failures(i32) | until(i64)`
pub struct ShareBundle;

impl ShareBundle {
    pub const SIZE: usize = 50;
    const VERSION: u8 = 1;
    const MAGIC: [u8; 4] = *b"ASTB";
    const SHARE_OFFSET: usize = 6;
    const FAILURES_OFFSET: usize = 38;
    const UNTIL_OFFSET: usize = 42;
    pub const MAX_FAILURES: i32 = 10;

    /// 解码槽位值：
    ///   null / 32B legacy / 合法 bundle / 其他损坏 → (share, failures, until)。
    pub fn decode(raw: Option<&[u8]>) -> (Option<Vec<u8>>, i32, i64) {
        let raw = match raw {
            None => return (None, 0, 0),
            Some(r) => r,
        };
        if raw.len() == KEY_SIZE {
            return (Some(raw.to_vec()), 0, 0);
        }
        if raw.len() >= Self::SIZE
            && raw[0..4] == Self::MAGIC
            && raw[4] == Self::VERSION
            && raw[5] as usize == KEY_SIZE
        {
            let failures = i32::from_le_bytes([
                raw[Self::FAILURES_OFFSET],
                raw[Self::FAILURES_OFFSET + 1],
                raw[Self::FAILURES_OFFSET + 2],
                raw[Self::FAILURES_OFFSET + 3],
            ])
            .clamp(0, Self::MAX_FAILURES);
            let until = i64::from_le_bytes([
                raw[Self::UNTIL_OFFSET],
                raw[Self::UNTIL_OFFSET + 1],
                raw[Self::UNTIL_OFFSET + 2],
                raw[Self::UNTIL_OFFSET + 3],
                raw[Self::UNTIL_OFFSET + 4],
                raw[Self::UNTIL_OFFSET + 5],
                raw[Self::UNTIL_OFFSET + 6],
                raw[Self::UNTIL_OFFSET + 7],
            ]);
            let share = raw[Self::SHARE_OFFSET..Self::SHARE_OFFSET + KEY_SIZE].to_vec();
            return (Some(share), failures, until);
        }
        (None, 0, 0)
    }

    /// 编码份额 + 退避状态。
    pub fn encode(share: &[u8], failures: i32, until: i64) -> Result<Vec<u8>> {
        if share.len() != KEY_SIZE {
            return Err(VaultError::argument(format!("份额须为 {KEY_SIZE} 字节")));
        }
        let mut buf = vec![0u8; Self::SIZE];
        buf[0..4].copy_from_slice(&Self::MAGIC);
        buf[4] = Self::VERSION;
        buf[5] = KEY_SIZE as u8;
        buf[Self::SHARE_OFFSET..Self::SHARE_OFFSET + KEY_SIZE].copy_from_slice(share);
        let f = failures.clamp(0, Self::MAX_FAILURES);
        buf[Self::FAILURES_OFFSET..Self::FAILURES_OFFSET + 4].copy_from_slice(&f.to_le_bytes());
        buf[Self::UNTIL_OFFSET..Self::UNTIL_OFFSET + 8].copy_from_slice(&until.to_le_bytes());
        Ok(buf)
    }
}

// ── 后端实现 ────────────────────────────────────────────────────────

/// 自包含模式（slots）的无操作后端。
pub struct NullSecretStore;

impl SecretStore for NullSecretStore {
    fn backend(&self) -> &str {
        "none"
    }
    fn load(&self, _key: &str) -> Result<Option<Vec<u8>>> {
        Ok(None)
    }
    fn store(&self, _key: &str, _value: &[u8]) -> Result<()> {
        Ok(())
    }
    fn delete(&self, _key: &str) -> Result<()> {
        Ok(())
    }
}

/// 文件密钥后端：K 整体落盘 `<dataDir>/secret.key`（0600 原子写）。
pub struct FileStore {
    data_dir: String,
}

impl FileStore {
    pub fn new(data_dir: &str) -> Self {
        Self {
            data_dir: data_dir.to_string(),
        }
    }
    fn key_path(&self) -> PathBuf {
        Path::new(&self.data_dir).join("secret.key")
    }
}

impl SecretStore for FileStore {
    fn backend(&self) -> &str {
        "file"
    }
    fn load(&self, _key: &str) -> Result<Option<Vec<u8>>> {
        let raw = match crate::safefile::read_regular_limited(&self.key_path(), 4096)? {
            Some(v) => v,
            None => return Ok(None),
        };
        // 槽位值：legacy 32B 原始 K，或含退避状态的 ShareBundle。
        if raw.len() == KEY_SIZE || raw.len() >= ShareBundle::SIZE {
            Ok(Some(raw))
        } else {
            Ok(None)
        }
    }
    fn store(&self, _key: &str, value: &[u8]) -> Result<()> {
        // 私有原子写（0600，tmp 独占、拒绝符号链接/FIFO）：K 的落盘路径同样受
        // 「符号链接穿透写 / FIFO 挂死」防线保护。
        crate::safefile::write_atomic(&self.key_path(), value, true)
    }
    fn delete(&self, _key: &str) -> Result<()> {
        let path = self.key_path();
        if path.exists() {
            std::fs::remove_file(&path)?;
        }
        Ok(())
    }
}

/// Linux Secret Service（libsecret / `org.freedesktop.Secret`）后端。
///
/// **2026-10 由 V1 桩补为真实现**（见 `src/secret_service.rs`）：C# 时代的 vault 走 libsecret/GLib
/// 能在此处读写份额，Rust 重写时只留了「未实现」桩，导致 Linux 上 `crypto`（LEGACY）无法启用
/// ——这正是「切不回 LEGACY」的根因。现按 D-Bus 线协议直连（纯 Rust、无新依赖；沙箱已放行 AF_UNIX）。
///
/// 兼容性：`xdg:schema = archoera.vault` + `account = "{key}|{dataDir}"` 与 C# 逐字一致 → 老条目可读。
/// fail-closed：无 session bus / 钥匙串锁定需交互 / 服务拒绝 / 结构不符 → 一律返回错误或视为无份额，
/// **绝不静默降级**（由 `SecretStoreFactory` 决定是否改用 file 后端）。
#[cfg(all(unix, not(target_os = "macos")))]
pub struct LibsecretStore {
    data_dir: String,
}

#[cfg(all(unix, not(target_os = "macos")))]
impl LibsecretStore {
    pub fn new(data_dir: &str) -> Result<Self> {
        if std::env::var("DBUS_SESSION_BUS_ADDRESS")
            .map(|v| v.is_empty())
            .unwrap_or(true)
        {
            return Err(VaultError::SecretStoreUnavailable(
                "Linux Secret Service 不可用（无 D-Bus session bus）：凭据持久化需降级为口令派生或禁用"
                    .to_string(),
            ));
        }
        Ok(Self {
            data_dir: data_dir.to_string(),
        })
    }

    fn bus() -> Result<crate::secret_service::SessionBus> {
        crate::secret_service::SessionBus::connect()
    }
}

#[cfg(all(unix, not(target_os = "macos")))]
impl SecretStore for LibsecretStore {
    fn backend(&self) -> &str {
        "libsecret"
    }
    fn load(&self, key: &str) -> Result<Option<Vec<u8>>> {
        let mut bus = Self::bus()?;
        crate::secret_service::lookup_secret(
            &mut bus,
            crate::secret_service::LEGACY_SCHEMA,
            key,
            &self.data_dir,
        )
    }
    fn store(&self, key: &str, value: &[u8]) -> Result<()> {
        let mut bus = Self::bus()?;
        crate::secret_service::store_secret(
            &mut bus,
            crate::secret_service::LEGACY_SCHEMA,
            key,
            &self.data_dir,
            value,
        )
    }
    fn delete(&self, key: &str) -> Result<()> {
        let mut bus = Self::bus()?;
        crate::secret_service::delete_secret(
            &mut bus,
            crate::secret_service::LEGACY_SCHEMA,
            key,
            &self.data_dir,
        )
    }
}

// ── Windows：DPAPI（CryptProtectData/CryptUnprotectData）─────────────

/// Windows DPAPI 后端：K 经系统账户/凭据绑定加密后落盘 `<dataDir>/sshare.bin`。
/// 与 C# `DpapiStore` 语义一致：单槽（忽略 `key`）、`CRYPTPROTECT_UI_FORBIDDEN`。
/// 文件型后端按目录落盘隔离，无需 account 作用域。
#[cfg(windows)]
pub struct DpapiStore {
    file: String,
}

#[cfg(windows)]
impl DpapiStore {
    pub fn new(data_dir: &str) -> Self {
        Self {
            file: Path::new(data_dir)
                .join("sshare.bin")
                .to_string_lossy()
                .into_owned(),
        }
    }
}

#[cfg(windows)]
impl SecretStore for DpapiStore {
    fn backend(&self) -> &str {
        "dpapi"
    }

    fn load(&self, _key: &str) -> Result<Option<Vec<u8>>> {
        use zeroize::Zeroize;
        let raw = match crate::safefile::read_regular_limited(Path::new(&self.file), 65536)? {
            Some(v) => v,
            None => return Ok(None),
        };
        let mut cipher = raw;
        let plain = dpapi::unprotect(&cipher);
        cipher.zeroize();
        Ok(Some(plain?))
    }

    fn store(&self, _key: &str, value: &[u8]) -> Result<()> {
        let cipher = dpapi::protect(value)?;
        crate::safefile::write_atomic(Path::new(&self.file), &cipher, false)
    }

    fn delete(&self, _key: &str) -> Result<()> {
        let path = Path::new(&self.file);
        if path.exists() {
            std::fs::remove_file(path)?;
        }
        Ok(())
    }
}

/// DPAPI 转换（`CryptProtectData` / `CryptUnprotectData`，`windows` crate 绑定）。
#[cfg(windows)]
mod dpapi {
    use super::{Result, VaultError};
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{LocalFree, HLOCAL};
    use windows::Win32::Security::Cryptography::{
        CryptProtectData, CryptUnprotectData, CRYPT_INTEGER_BLOB, CRYPTPROTECT_UI_FORBIDDEN,
    };

    /// 输入 blob：只读借用，`CryptProtectData` 不修改其内容。
    fn input_blob(bytes: &[u8]) -> CRYPT_INTEGER_BLOB {
        CRYPT_INTEGER_BLOB {
            cbData: bytes.len() as u32,
            pbData: bytes.as_ptr() as *mut u8,
        }
    }

    /// 复制输出 blob 并 `LocalFree` 其原生缓冲（Crypt32 分配）。
    fn take_blob(blob: CRYPT_INTEGER_BLOB) -> Vec<u8> {
        let out = if blob.pbData.is_null() || blob.cbData == 0 {
            Vec::new()
        } else {
            // SAFETY: 成功返回时 Crypt32 保证 pbData 指向 cbData 个可读字节。
            unsafe { std::slice::from_raw_parts(blob.pbData, blob.cbData as usize).to_vec() }
        };
        if !blob.pbData.is_null() {
            // SAFETY: pbData 由 Crypt32 分配，须经 LocalFree 释放（文档要求）。
            unsafe {
                let _ = LocalFree(HLOCAL(blob.pbData as *mut core::ffi::c_void));
            }
        }
        out
    }

    pub fn protect(plain: &[u8]) -> Result<Vec<u8>> {
        let input = input_blob(plain);
        let mut out = CRYPT_INTEGER_BLOB::default();
        // SAFETY: input 在调用期间存活；out 由 API 写出。
        unsafe {
            CryptProtectData(
                &input,
                PCWSTR::null(),
                None,
                None,
                None,
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut out,
            )
            .map_err(|e| {
                VaultError::invalid_operation(format!("CryptProtectData 失败（Win32={}）", e.code().0))
            })?;
        }
        Ok(take_blob(out))
    }

    pub fn unprotect(cipher: &[u8]) -> Result<Vec<u8>> {
        let input = input_blob(cipher);
        let mut out = CRYPT_INTEGER_BLOB::default();
        // SAFETY: 同上；pps zDataDescr 传 None（不需要描述文本）。
        unsafe {
            CryptUnprotectData(&input, None, None, None, None, CRYPTPROTECT_UI_FORBIDDEN, &mut out)
                .map_err(|e| {
                    VaultError::invalid_operation(format!(
                        "CryptUnprotectData 失败（Win32={}）",
                        e.code().0
                    ))
                })?;
        }
        Ok(take_blob(out))
    }
}

// ── macOS：Keychain（kSecClassGenericPassword）──────────────────────

/// macOS Keychain 后端：S 以原始字节存钥匙串，service `archoera.vault`，
/// account 按数据目录作用域（`{key}|{dataDir}`，与 C# `SecretKeyScope` 一致）。
/// store 仅写 scoped account；load 先 scoped 后 legacy 回退；delete 两者皆删。
#[cfg(target_os = "macos")]
pub struct KeychainStore {
    data_dir: String,
}

#[cfg(target_os = "macos")]
impl KeychainStore {
    /// 钥匙串 service 名（与 C# `KeychainStore.Service` 一致）。
    const SERVICE: &'static str = "archoera.vault";
    /// errSecItemNotFound：条目不存在（非错误，等价「无份额」）。
    const ERR_SEC_ITEM_NOT_FOUND: i32 = -25300;

    pub fn new(data_dir: &str) -> Self {
        Self {
            data_dir: data_dir.to_string(),
        }
    }

    /// 数据目录作用域化 account（防跨目录覆盖；见 `SecretKeyScope`）。
    fn scoped(&self, key: &str) -> String {
        format!("{key}|{}", self.data_dir)
    }

    fn load_acct(account: &str) -> Result<Option<Vec<u8>>> {
        use security_framework::passwords::get_generic_password;
        match get_generic_password(Self::SERVICE, account) {
            Ok(v) => Ok(Some(v)),
            Err(e) if e.code() == Self::ERR_SEC_ITEM_NOT_FOUND => Ok(None),
            Err(e) => Err(VaultError::SecretStoreUnavailable(format!(
                "Keychain 读取失败（status={}）",
                e.code()
            ))),
        }
    }

    fn store_acct(account: &str, value: &[u8]) -> Result<()> {
        use security_framework::passwords::set_generic_password;
        // set_generic_password 对同 service+account 为 upsert（等价 C# 先删后加）。
        set_generic_password(Self::SERVICE, account, value).map_err(|e| {
            VaultError::SecretStoreUnavailable(format!("Keychain 写入失败（status={}）", e.code()))
        })
    }

    fn delete_acct(account: &str) {
        use security_framework::passwords::delete_generic_password;
        // SecItemDelete：条目不存在亦算成功，忽略错误（与 C# 一致）。
        let _ = delete_generic_password(Self::SERVICE, account);
    }
}

#[cfg(target_os = "macos")]
impl SecretStore for KeychainStore {
    fn backend(&self) -> &str {
        "keychain"
    }

    fn load(&self, key: &str) -> Result<Option<Vec<u8>>> {
        // scoped 优先；legacy 回退（升级前创建的旧库存于未作用域化槽位）
        if let Some(v) = Self::load_acct(&self.scoped(key))? {
            return Ok(Some(v));
        }
        Self::load_acct(key)
    }

    fn store(&self, key: &str, value: &[u8]) -> Result<()> {
        Self::store_acct(&self.scoped(key), value)
    }

    fn delete(&self, key: &str) -> Result<()> {
        Self::delete_acct(&self.scoped(key));
        Self::delete_acct(key); // 清理升级前 legacy 槽
        Ok(())
    }
}

/// 读 vault 文件头记录的后端指纹（无文件 / 头损坏 → None）。
fn read_vault_backend(data_dir: &str) -> Option<String> {
    let path = Path::new(data_dir).join(crate::service::VAULT_FILE_NAME);
    if !path.exists() {
        return None;
    }
    crate::format::read_header(&path.to_string_lossy())
        .ok()
        .and_then(|h| h.backend)
}

/// 平台安全存储工厂。
pub fn secret_store_factory(data_dir: &str) -> Result<Box<dyn SecretStore>> {
    let backend = read_vault_backend(data_dir);
    if backend.as_deref() == Some("file") {
        return Ok(Box::new(FileStore::new(data_dir)));
    }
    if std::env::var("ARCHOERA_VAULT_BACKEND").as_deref() == Ok("file") && backend.is_none() {
        return Ok(Box::new(FileStore::new(data_dir)));
    }
    let _ = &backend;
    #[cfg(windows)]
    {
        return Ok(Box::new(DpapiStore::new(data_dir)));
    }
    #[cfg(target_os = "macos")]
    {
        return Ok(Box::new(KeychainStore::new(data_dir)));
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        return Ok(Box::new(LibsecretStore::new(data_dir)?));
    }
    #[cfg(not(any(unix, windows)))]
    {
        Err(VaultError::SecretStoreUnavailable(
            "不支持的平台：无可用 OS 安全存储".to_string(),
        ))
    }
}
