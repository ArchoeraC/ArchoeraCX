// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! vault 服务（命令语义）：init-crypto / init-file / init-m1 / init-m2a / init-m2b /
//! serve / destroy / status（与 C# VaultService.cs 行为一致）。

use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

use subtle::ConstantTimeEq;
use zeroize::{Zeroize, Zeroizing};

use crate::crypto::{self, KdfParams};
use crate::memguard::LockedBuffer;
use crate::device;
use crate::errors::{Result, VaultError};
use crate::format::{
    self, build_slot_ad, detect_slots_mode, validate_kdf, Slot, SlotKind, SlotsMode, VaultFile,
    VaultMode, ALG_AES256GCM, KEY_SIZE,
};
use crate::lockout::VaultLockout;
use crate::recovery;
use crate::safefile::DataDirLock;
use crate::share::{self, NullSecretStore, SecretStore};

pub const VAULT_FILE_NAME: &str = "credentials.vault";
pub const AUTH_UID: &str = "__auth__";

const DEVICE_INFO: &str = "archoera.slot.device";
const RECOVERY_INFO: &str = "archoera.slot.recovery";
const PWDEV_INFO: &str = "archoera.slot.pwdev";

pub struct VaultService {
    data_dir: String,
    store: Box<dyn SecretStore>,
    lockout: VaultLockout,
}

impl VaultService {
    /// 默认后端选择（与 C# SelectDefaultStore 一致）：已存在 'S' vault → NullSecretStore；
    /// 其余 → 工厂分发。
    pub fn new(data_dir: &str) -> Result<Self> {
        let store = Self::select_default_store(data_dir)?;
        Ok(Self {
            data_dir: data_dir.to_string(),
            store,
            lockout: VaultLockout::new(data_dir),
        })
    }

    /// 显式注入后端（等价 C# `new VaultService(dataDir, store)`）。
    pub fn with_store(data_dir: &str, store: Box<dyn SecretStore>) -> Self {
        Self {
            data_dir: data_dir.to_string(),
            store,
            lockout: VaultLockout::new(data_dir),
        }
    }

    fn select_default_store(data_dir: &str) -> Result<Box<dyn SecretStore>> {
        let path = Path::new(data_dir).join(VAULT_FILE_NAME);
        if path.exists() {
            if let Ok(h) = format::read_header(&path.to_string_lossy()) {
                if h.mode == VaultMode::Slots {
                    return Ok(Box::new(NullSecretStore));
                }
            }
        }
        share::secret_store_factory(data_dir)
    }

    pub fn vault_path(&self) -> String {
        Path::new(&self.data_dir)
            .join(VAULT_FILE_NAME)
            .to_string_lossy()
            .into_owned()
    }

    pub fn initialized(&self) -> bool {
        Path::new(&self.vault_path()).exists()
    }

    pub fn backend(&self) -> &str {
        self.store.backend()
    }

    pub fn store(&self) -> &dyn SecretStore {
        &*self.store
    }

    pub fn lockout(&self) -> &VaultLockout {
        &self.lockout
    }

    pub fn mode(&self) -> Result<VaultMode> {
        if !self.initialized() {
            return Ok(VaultMode::Crypto);
        }
        Ok(format::read_header(&self.vault_path())?.mode)
    }

    pub fn slots_mode(&self) -> Result<SlotsMode> {
        if !self.initialized() {
            return Ok(SlotsMode::None);
        }
        let h = format::read_header(&self.vault_path())?;
        if h.mode == VaultMode::Slots {
            Ok(detect_slots_mode(&h.slot_kinds))
        } else {
            Ok(SlotsMode::None)
        }
    }

    pub fn current_kdf(&self) -> Result<Option<KdfParams>> {
        if !self.initialized() {
            return Ok(None);
        }
        Ok(format::read_header(&self.vault_path())?.kdf)
    }

    fn ensure_initialized(&self) -> Result<()> {
        if !self.initialized() {
            return Err(VaultError::invalid_operation("vault 未初始化，请先初始化"));
        }
        Ok(())
    }

    // ── 初始化 ──────────────────────────────────────────────────────

    /// LEGACY crypto 单因子：K 整体入后端，vault 文件仅记后端指纹。
    pub fn init_crypto(&self, master: Option<Vec<u8>>) -> Result<()> {
        if self.initialized() {
            return Err(VaultError::invalid_operation("vault 已初始化，请先 destroy"));
        }
        // 跨进程写锁：初始化亦是「读-改-写」，与并发会话互斥（见 DataDirLock）。
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        if let Some(m) = &master {
            if m.len() != KEY_SIZE {
                return Err(VaultError::argument(format!("主密钥须为 {KEY_SIZE} 字节")));
            }
        }
        self.lockout.clear(self.store());
        // K（crypto 模式即主密钥）短时持有：Zeroizing 保证用后零化。
        let k = Zeroizing::new(match master {
            Some(mut m) => {
                let v = m.clone();
                m.zeroize();
                v
            }
            None => crypto::random_vec(KEY_SIZE)?,
        });
        let result = (|| -> Result<()> {
            self.lockout.set_share(self.store(), &k)?;
            let file = VaultFile {
                mode: VaultMode::Crypto,
                backend: Some(self.store.backend().to_string()),
                vault_id: None,
                created_at_ms: 0,
                alg: 0,
                slots: Vec::new(),
                entries: Vec::new(),
                trailer: None,
                mac_input: None,
            };
            format::save(&self.vault_path(), &file, None)
        })();
        if result.is_err() {
            let _ = std::fs::remove_file(self.vault_path());
        }
        result
    }

    /// 'S' slots（F1 = 仅口令槽）。
    pub fn init_slots(&self, password: &[u8], kdf: Option<KdfParams>) -> Result<()> {
        if self.initialized() {
            return Err(VaultError::invalid_operation("vault 已初始化，请先 destroy"));
        }
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        self.lockout.clear(self.store());
        let kdf = kdf.unwrap_or(KdfParams::DEFAULT);
        validate_kdf(kdf.m_kib, kdf.t, kdf.p)?;
        let vault_id = crypto::random_vec(format::VAULT_ID_SIZE)?;
        let created = now_ms();
        let master = Zeroizing::new(crypto::random_vec(KEY_SIZE)?);
        let salt = crypto::random_vec(format::SALT_SIZE)?;
        let ad = build_slot_ad(&vault_id, SlotKind::Password);
        let kek = LockedBuffer::take(crypto::argon2id(password, &salt, &ad, kdf, KEY_SIZE)?);
        let slot = format::wrap_slot(
            SlotKind::Password,
            Some(kdf),
            salt,
            kek.as_slice(),
            &master,
            &ad,
        )?;
        let file = VaultFile {
            mode: VaultMode::Slots,
            backend: None,
            vault_id: Some(vault_id),
            created_at_ms: created,
            alg: ALG_AES256GCM,
            slots: vec![slot],
            entries: Vec::new(),
            trailer: None,
            mac_input: None,
        };
        let result = format::save(&self.vault_path(), &file, Some(&master));
        if result.is_err() {
            let _ = std::fs::remove_file(self.vault_path());
        }
        result
    }

    /// 'S' slots（F2 m2a = 口令+设备 + 恢复槽）；返回恢复码展示串。
    pub fn init_m2a(&self, password: &[u8], kdf: Option<KdfParams>) -> Result<String> {
        if self.initialized() {
            return Err(VaultError::invalid_operation("vault 已初始化，请先 destroy"));
        }
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        self.lockout.clear(self.store());
        let kdf = kdf.unwrap_or(KdfParams::DEFAULT);
        validate_kdf(kdf.m_kib, kdf.t, kdf.p)?;
        let vault_id = crypto::random_vec(format::VAULT_ID_SIZE)?;
        let created = now_ms();
        let master = Zeroizing::new(crypto::random_vec(KEY_SIZE)?);
        let salt = crypto::random_vec(format::SALT_SIZE)?;
        let device_secret = Zeroizing::new(device::create_and_save(&self.data_dir)?);
        let recovery_bytes = Zeroizing::new(crypto::random_vec(recovery::BYTE_SIZE)?);
        let code = recovery::encode(&recovery_bytes);

        let build = || -> Result<VaultFile> {
            let pd_ad = build_slot_ad(&vault_id, SlotKind::PasswordDevice);
            let base_kek = LockedBuffer::take(crypto::argon2id(
                password, &salt, &pd_ad, kdf, KEY_SIZE,
            )?);
            let mut ikm = Zeroizing::new(base_kek.as_slice().to_vec());
            ikm.extend_from_slice(&device_secret);
            let pd_kek = LockedBuffer::take(crypto::hkdf_sha256(
                &ikm,
                &salt,
                PWDEV_INFO.as_bytes(),
                KEY_SIZE,
            ));
            let pd_slot = format::wrap_slot(
                SlotKind::PasswordDevice,
                Some(kdf),
                salt.clone(),
                pd_kek.as_slice(),
                &master,
                &pd_ad,
            )?;

            let rec_salt = crypto::random_vec(format::SALT_SIZE)?;
            let rec_ad = build_slot_ad(&vault_id, SlotKind::Recovery);
            let rec_kek = LockedBuffer::take(crypto::hkdf_sha256(
                &recovery_bytes,
                &rec_salt,
                RECOVERY_INFO.as_bytes(),
                KEY_SIZE,
            ));
            let rec_slot = format::wrap_slot(
                SlotKind::Recovery,
                None,
                rec_salt,
                rec_kek.as_slice(),
                &master,
                &rec_ad,
            )?;

            Ok(VaultFile {
                mode: VaultMode::Slots,
                backend: None,
                vault_id: Some(vault_id.clone()),
                created_at_ms: created,
                alg: ALG_AES256GCM,
                slots: vec![pd_slot, rec_slot],
                entries: Vec::new(),
                trailer: None,
                mac_input: None,
            })
        };
        let file = match build() {
            Ok(f) => f,
            Err(e) => {
                let _ = std::fs::remove_file(self.vault_path());
                let _ = device::delete(&self.data_dir);
                return Err(e);
            }
        };
        let result = format::save(&self.vault_path(), &file, Some(&master));
        if result.is_err() {
            let _ = std::fs::remove_file(self.vault_path());
            let _ = device::delete(&self.data_dir);
        }
        result.map(|_| code)
    }

    /// 'S' slots（F2 m2b = 仅设备槽）。
    pub fn init_m2b(&self) -> Result<()> {
        if self.initialized() {
            return Err(VaultError::invalid_operation("vault 已初始化，请先 destroy"));
        }
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        self.lockout.clear(self.store());
        let vault_id = crypto::random_vec(format::VAULT_ID_SIZE)?;
        let created = now_ms();
        let master = Zeroizing::new(crypto::random_vec(KEY_SIZE)?);
        let device_secret = Zeroizing::new(device::create_and_save(&self.data_dir)?);
        let salt = crypto::random_vec(format::SALT_SIZE)?;
        let ad = build_slot_ad(&vault_id, SlotKind::Device);
        let kek = LockedBuffer::take(crypto::hkdf_sha256(
            &device_secret,
            &salt,
            DEVICE_INFO.as_bytes(),
            KEY_SIZE,
        ));
        let build = || -> Result<VaultFile> {
            let slot = format::wrap_slot(SlotKind::Device, None, salt, kek.as_slice(), &master, &ad)?;
            Ok(VaultFile {
                mode: VaultMode::Slots,
                backend: None,
                vault_id: Some(vault_id.clone()),
                created_at_ms: created,
                alg: ALG_AES256GCM,
                slots: vec![slot],
                entries: Vec::new(),
                trailer: None,
                mac_input: None,
            })
        };
        let file = match build() {
            Ok(f) => f,
            Err(e) => {
                let _ = std::fs::remove_file(self.vault_path());
                let _ = device::delete(&self.data_dir);
                return Err(e);
            }
        };
        let result = format::save(&self.vault_path(), &file, Some(&master));
        if result.is_err() {
            let _ = std::fs::remove_file(self.vault_path());
            let _ = device::delete(&self.data_dir);
        }
        result
    }

    // ── 重键 KDF（不丢数据）────────────────────────────────────────

    pub fn change_kdf(&self, password: &[u8], kdf: KdfParams) -> Result<()> {
        self.ensure_initialized()?;
        validate_kdf(kdf.m_kib, kdf.t, kdf.p)?;
        // 跨进程写锁：重键同样是「读-改-写」（耗时可达秒级，正是丢失更新最易发生的窗口）。
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        let mut vault = format::load(&self.vault_path())?;
        if vault.mode != VaultMode::Slots
            || vault.vault_id.is_none()
            || vault.mac_input.is_none()
            || vault.trailer.is_none()
        {
            return Err(VaultError::invalid_operation("该模式不支持更改 KDF"));
        }
        let kinds: Vec<SlotKind> = vault.slots.iter().map(|s| s.kind).collect();
        let slot_mode = detect_slots_mode(&kinds);
        let target = match slot_mode {
            SlotsMode::M1 => SlotKind::Password,
            SlotsMode::M2a => SlotKind::PasswordDevice,
            _ => return Err(VaultError::invalid_operation("该模式不支持更改 KDF")),
        };
        let index = vault
            .slots
            .iter()
            .position(|s| s.kind == target)
            .ok_or_else(|| VaultError::invalid_operation("vault 缺少口令槽位，无法更改 KDF"))?;
        let slot = vault.slots[index].clone();
        let vault_id = vault.vault_id.clone().unwrap();
        let ad = build_slot_ad(&vault_id, slot.kind);
        let new_salt = crypto::random_vec(format::SALT_SIZE)?;
        let new_nonce = crypto::random_vec(format::NONCE_SIZE)?;
        let device_secret = if slot.kind == SlotKind::PasswordDevice {
            Some(Zeroizing::new(device::load(&self.data_dir)?))
        } else {
            None
        };

        let old_base = LockedBuffer::take(crypto::argon2id(
            password, &slot.salt, &ad, slot.kdf, KEY_SIZE,
        )?);
        let old_kek = match &device_secret {
            Some(ds) => {
                let mut ikm = Zeroizing::new(old_base.as_slice().to_vec());
                ikm.extend_from_slice(ds);
                LockedBuffer::take(crypto::hkdf_sha256(
                    &ikm,
                    &slot.salt,
                    PWDEV_INFO.as_bytes(),
                    KEY_SIZE,
                ))
            }
            None => old_base,
        };
        let k = Zeroizing::new(crypto::aes256gcm_decrypt(
            old_kek.as_slice(),
            &slot.nonce,
            &slot.wrapped,
            &ad,
        )?);

        let mac = format::compute_slots_mac(&k, vault.mac_input.as_ref().unwrap());
        if !bool::from(mac.ct_eq(vault.trailer.as_ref().unwrap().as_slice())) {
            return Err(VaultError::auth_tag(
                "slots vault 尾部 HMAC 校验失败（header/条目被篡改）",
            ));
        }

        let new_base = LockedBuffer::take(crypto::argon2id(
            password, &new_salt, &ad, kdf, KEY_SIZE,
        )?);
        let new_kek = match &device_secret {
            Some(ds) => {
                let mut ikm = Zeroizing::new(new_base.as_slice().to_vec());
                ikm.extend_from_slice(ds);
                LockedBuffer::take(crypto::hkdf_sha256(
                    &ikm,
                    &new_salt,
                    PWDEV_INFO.as_bytes(),
                    KEY_SIZE,
                ))
            }
            None => new_base,
        };
        let wrapped = crypto::aes256gcm_encrypt(new_kek.as_slice(), &new_nonce, &k, &ad)?;
        vault.slots[index] = Slot {
            kind: slot.kind,
            kdf,
            salt: new_salt,
            nonce: new_nonce,
            wrapped,
        };
        format::save(&self.vault_path(), &vault, Some(&k))
    }

    // ── 解锁 ────────────────────────────────────────────────────────

    /// 重建主密钥到**锁定缓冲**（mlock/VirtualLock；slots：secret/label 语义随因子模式；
    /// crypto：整体取回）。返回的 [LockedBuffer] 在 drop 时 zeroize + 解锁。
    pub fn load_master_key_into(
        &self,
        secret: Option<&[u8]>,
        label: Option<&str>,
    ) -> Result<LockedBuffer> {
        let vault = format::load(&self.vault_path())?;
        if vault.mode == VaultMode::Slots {
            let vault_id = vault.vault_id.clone().ok_or_else(|| {
                VaultError::invalid_data("slots vault 头部不完整（缺 vaultId/HMAC），拒绝加载")
            })?;
            let mac_input = vault.mac_input.clone().ok_or_else(|| {
                VaultError::invalid_data("slots vault 头部不完整（缺 vaultId/HMAC），拒绝加载")
            })?;
            let trailer = vault.trailer.clone().ok_or_else(|| {
                VaultError::invalid_data("slots vault 头部不完整（缺 vaultId/HMAC），拒绝加载")
            })?;
            let kinds: Vec<SlotKind> = vault.slots.iter().map(|s| s.kind).collect();
            let slot_mode = detect_slots_mode(&kinds);
            let (slot, kek) = self.select_slot_and_kek(&vault, slot_mode, secret, label)?;
            let ad = build_slot_ad(&vault_id, slot.kind);
            let k = LockedBuffer::take(crypto::aes256gcm_decrypt(
                kek.as_slice(),
                &slot.nonce,
                &slot.wrapped,
                &ad,
            )?);
            let mac = format::compute_slots_mac(k.as_slice(), &mac_input);
            if !bool::from(mac.ct_eq(trailer.as_slice())) {
                return Err(VaultError::auth_tag(
                    "slots vault 尾部 HMAC 校验失败（header/条目被篡改）",
                ));
            }
            return Ok(k);
        }
        if vault.mode == VaultMode::Crypto {
            if let Some(b) = &vault.backend {
                if b != self.store.backend() {
                    return Err(VaultError::ShareBackendMismatch {
                        stored: b.clone(),
                        current: self.store.backend().to_string(),
                    });
                }
            }
            let k = self
                .lockout
                .get_share(self.store())?
                .ok_or_else(|| VaultError::ShareMissing(self.store.backend().to_string()))?;
            return Ok(LockedBuffer::take(k));
        }
        Err(VaultError::invalid_data("不支持的 vault 模式，拒绝加载"))
    }

    fn select_slot_and_kek(
        &self,
        vault: &VaultFile,
        slot_mode: SlotsMode,
        secret: Option<&[u8]>,
        label: Option<&str>,
    ) -> Result<(Slot, LockedBuffer)> {
        let vault_id = vault.vault_id.as_ref().unwrap();
        match slot_mode {
            SlotsMode::M1 => {
                if label.is_some() {
                    return Err(VaultError::invalid_operation("m1：不接受槽位标签"));
                }
                let secret = secret.ok_or_else(|| {
                    VaultError::invalid_operation("slots 模式：解锁需要口令（握手第 4 字段）")
                })?;
                let slot = vault
                    .slots
                    .iter()
                    .find(|s| s.kind == SlotKind::Password)
                    .ok_or_else(|| VaultError::invalid_operation("vault 无口令槽位，无法解锁"))?
                    .clone();
                let ad = build_slot_ad(vault_id, slot.kind);
                let kek = LockedBuffer::take(crypto::argon2id(
                    secret, &slot.salt, &ad, slot.kdf, KEY_SIZE,
                )?);
                Ok((slot, kek))
            }
            SlotsMode::M2a => {
                if label == Some("rec") {
                    let secret = secret.ok_or_else(|| {
                        VaultError::invalid_operation("m2a 恢复路径：需要恢复码（握手第 4 字段）")
                    })?;
                    let slot = vault
                        .slots
                        .iter()
                        .find(|s| s.kind == SlotKind::Recovery)
                        .ok_or_else(|| {
                            VaultError::invalid_operation("vault 无恢复槽位，无法用恢复码解锁")
                        })?
                        .clone();
                    let code_bytes = Zeroizing::new(recovery::decode_utf8(secret)?);
                    let kek = LockedBuffer::take(crypto::hkdf_sha256(
                        &code_bytes,
                        &slot.salt,
                        RECOVERY_INFO.as_bytes(),
                        KEY_SIZE,
                    ));
                    Ok((slot, kek))
                } else if let Some(l) = label {
                    Err(VaultError::invalid_operation(format!("m2a：未知槽位标签 [{l}]")))
                } else {
                    let secret = secret.ok_or_else(|| {
                        VaultError::invalid_operation("m2a：解锁需要口令（握手第 4 字段）")
                    })?;
                    let pd_slot = vault
                        .slots
                        .iter()
                        .find(|s| s.kind == SlotKind::PasswordDevice)
                        .ok_or_else(|| {
                            VaultError::invalid_operation("vault 无口令+设备槽位，无法解锁")
                        })?
                        .clone();
                    let device_secret = Zeroizing::new(device::load(&self.data_dir)?);
                    let ad = build_slot_ad(vault_id, pd_slot.kind);
                    let base = LockedBuffer::take(crypto::argon2id(
                        secret, &pd_slot.salt, &ad, pd_slot.kdf, KEY_SIZE,
                    )?);
                    let mut ikm = Zeroizing::new(base.as_slice().to_vec());
                    ikm.extend_from_slice(&device_secret);
                    let kek = LockedBuffer::take(crypto::hkdf_sha256(
                        &ikm,
                        &pd_slot.salt,
                        PWDEV_INFO.as_bytes(),
                        KEY_SIZE,
                    ));
                    Ok((pd_slot, kek))
                }
            }
            SlotsMode::M2b => {
                if label.is_some() {
                    return Err(VaultError::invalid_operation(
                        "m2b：不接受槽位标签（握手仅 3 字段）",
                    ));
                }
                let slot = vault
                    .slots
                    .iter()
                    .find(|s| s.kind == SlotKind::Device)
                    .ok_or_else(|| VaultError::invalid_operation("vault 无设备槽位，无法解锁"))?
                    .clone();
                let device_secret = Zeroizing::new(device::load(&self.data_dir)?);
                let kek = LockedBuffer::take(crypto::hkdf_sha256(
                    &device_secret,
                    &slot.salt,
                    DEVICE_INFO.as_bytes(),
                    KEY_SIZE,
                ));
                Ok((slot, kek))
            }
            SlotsMode::None => Err(VaultError::invalid_data(
                "slots vault 槽位集合无法识别（无可用因子），拒绝加载",
            )),
        }
    }

    // ── 会话持钥路径 ────────────────────────────────────────────────

    pub fn get_or_create_auth_anchor(&self, master: &[u8]) -> Result<Vec<u8>> {
        // 跨进程写锁：首次会**写入**锚点条目（读-改-写）。
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        let mut vault = format::load(&self.vault_path())?;
        if let Some(entry) = vault.entries.iter().find(|e| e.uid == AUTH_UID) {
            return format::decrypt_entry(entry, master);
        }
        let t = Zeroizing::new(crypto::random_vec(16)?);
        let entry = format::encrypt_entry(AUTH_UID, &t, master)?;
        vault.entries.push(entry);
        format::save(&self.vault_path(), &vault, Some(master))?;
        Ok(t.to_vec())
    }

    pub fn get_with(&self, uid: &str, master: &[u8]) -> Result<Option<Vec<u8>>> {
        let vault = format::load(&self.vault_path())?;
        match vault.entries.iter().find(|e| e.uid == uid) {
            Some(e) => Ok(Some(format::decrypt_entry(e, master)?)),
            None => Ok(None),
        }
    }

    pub fn set_with(&self, uid: &str, plaintext: &[u8], master: &[u8]) -> Result<()> {
        // 跨进程写锁：加载 → 改 → 保存 必须原子（否则并发会话静默丢更新）。
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        let mut vault = format::load(&self.vault_path())?;
        vault.entries.retain(|e| e.uid != uid);
        let entry = format::encrypt_entry(uid, plaintext, master)?;
        vault.entries.push(entry);
        format::save(&self.vault_path(), &vault, Some(master))
    }

    pub fn delete_with(&self, uid: &str, master: &[u8]) -> Result<bool> {
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        let mut vault = format::load(&self.vault_path())?;
        let before = vault.entries.len();
        vault.entries.retain(|e| e.uid != uid);
        let removed = vault.entries.len() < before;
        if removed {
            format::save(&self.vault_path(), &vault, Some(master))?;
        }
        Ok(removed)
    }

    /// 全量销毁：份额 + vault 文件 + `.bak` + `device.key` + 锁定状态。
    pub fn destroy(&self) -> Result<()> {
        // 跨进程写锁：销毁与其它会话的写互斥（保留 `vault.lock` 本身，见 safefile::LOCK_NAME）。
        let _lock = DataDirLock::acquire(&self.data_dir)?;
        self.lockout.delete_share(self.store())?;
        let path = self.vault_path();
        let _ = std::fs::remove_file(&path);
        let _ = std::fs::remove_file(format!("{path}.bak"));
        // 遗留临时文件（`<vault>.tmp` 与带 pid 的 `<vault>.tmp.<pid>`）：一并清理，
        // 避免崩溃残留长期堆积（内容为密文，但没必要留）。
        if let Ok(entries) = std::fs::read_dir(&self.data_dir) {
            for e in entries.flatten() {
                let name = e.file_name();
                let name = name.to_string_lossy();
                if name.starts_with(&format!("{VAULT_FILE_NAME}.tmp")) {
                    let _ = std::fs::remove_file(e.path());
                }
            }
        }
        device::delete(&self.data_dir)?;
        self.lockout.clear(self.store());
        Ok(())
    }
}

pub fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}
