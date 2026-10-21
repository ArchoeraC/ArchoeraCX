// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! vault 文件格式（与 C# VaultFile.cs 字节兼容）。
//!
//! ```text
//! magic "AVLT"(4B) | version(1B) | 头部(按模式)… | entry_count(u32 LE) | entries… [| trailer]
//! 'C'(0x43) LEGACY：reserved(3B) | key_vault(32B 零占位) | backend_len(1B) | backend(utf8)
//! 'S'(0x53) slots ：vault_id(16B) | created_at_ms(i64 LE) | alg(1B=1) | slot_count(1B)
//!                   | slots… | entry_count | entries… | trailer(32B HMAC-SHA256(K, 前文))
//! entry: uid_len(1B) | uid(utf8) | salt(16B) | nonce(12B) | ct_len(u32 LE) | ct(ct‖tag)
//! slot : kind(1B) | m(u32) | t(u32) | p(u32) | salt(16B) | nonce(12B) | wrapped_len(u16) | wrapped
//! ```

use std::path::Path;

use crate::crypto::{self, KdfParams};
use crate::errors::{Result, VaultError};

pub const MAGIC: u32 = 0x544C_5641; // "AVLT"（小端）
pub const MODE_CRYPTO: u8 = b'C';
pub const MODE_SLOTS: u8 = b'S';

pub const KEY_SIZE: usize = 32;
pub const SALT_SIZE: usize = 16;
pub const NONCE_SIZE: usize = 12;
#[allow(dead_code)]
pub const TAG_SIZE: usize = 16;
pub const VAULT_ID_SIZE: usize = 16;
pub const ALG_AES256GCM: u8 = 1;
pub const TRAILER_SIZE: usize = 32;

pub const MAX_FILE_SIZE: u64 = 16 * 1024 * 1024;
pub const MAX_ENTRIES: u32 = 100_000;
pub const MAX_ENTRY_BYTES: u32 = 4 * 1024 * 1024;

// KDF 参数上下界。
pub const MIN_KDF_MEMORY_KIB: u32 = 8192;
pub const MAX_KDF_MEMORY_KIB: u32 = 256 * 1024;
pub const MIN_KDF_ITERATIONS: u32 = 1;
pub const MAX_KDF_ITERATIONS: u32 = 16;
pub const MIN_KDF_PARALLELISM: u32 = 1;
pub const MAX_KDF_PARALLELISM: u32 = 8;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VaultMode {
    Crypto,
    Slots,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SlotsMode {
    None,
    M1,
    M2a,
    M2b,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SlotKind {
    Password = 1,
    Device = 2,
    Recovery = 3,
    PasswordDevice = 4,
}

impl SlotKind {
    pub fn from_byte(b: u8) -> Result<Self> {
        match b {
            1 => Ok(Self::Password),
            2 => Ok(Self::Device),
            3 => Ok(Self::Recovery),
            4 => Ok(Self::PasswordDevice),
            _ => Err(VaultError::invalid_data(format!("未知槽位类型 {b}"))),
        }
    }
    pub fn as_byte(self) -> u8 {
        self as u8
    }
}

/// 校验 KDF 参数是否在允许区间（下界 F1 + 上界 SEC-06）。
pub fn validate_kdf(m: u32, t: u32, p: u32) -> Result<()> {
    if p < MIN_KDF_PARALLELISM || p > MAX_KDF_PARALLELISM {
        return Err(VaultError::invalid_data(format!(
            "KDF 并行度参数异常（p={p}），拒绝加载"
        )));
    }
    if m < MIN_KDF_MEMORY_KIB || m > MAX_KDF_MEMORY_KIB {
        return Err(VaultError::invalid_data(format!(
            "KDF 内存参数异常（m={m} KiB），拒绝加载"
        )));
    }
    if t < MIN_KDF_ITERATIONS || t > MAX_KDF_ITERATIONS {
        return Err(VaultError::invalid_data(format!(
            "KDF 迭代次数异常（t={t}），拒绝加载"
        )));
    }
    Ok(())
}

/// 校验（可为负的）KDF 参数并归一化为 u32（对应 C# int 解析 + ValidateKdf）。
pub fn validate_kdf_signed(m: i64, t: i64, p: i64) -> Result<(u32, u32, u32)> {
    if p < MIN_KDF_PARALLELISM as i64 || p > MAX_KDF_PARALLELISM as i64 {
        return Err(VaultError::invalid_data(format!(
            "KDF 并行度参数异常（p={p}），拒绝加载"
        )));
    }
    if m < MIN_KDF_MEMORY_KIB as i64 || m > MAX_KDF_MEMORY_KIB as i64 {
        return Err(VaultError::invalid_data(format!(
            "KDF 内存参数异常（m={m} KiB），拒绝加载"
        )));
    }
    if t < MIN_KDF_ITERATIONS as i64 || t > MAX_KDF_ITERATIONS as i64 {
        return Err(VaultError::invalid_data(format!(
            "KDF 迭代次数异常（t={t}），拒绝加载"
        )));
    }
    Ok((m as u32, t as u32, p as u32))
}

#[derive(Debug, Clone)]
pub struct Slot {
    pub kind: SlotKind,
    pub kdf: KdfParams,
    pub salt: Vec<u8>,
    pub nonce: Vec<u8>,
    pub wrapped: Vec<u8>,
}

#[derive(Debug, Clone)]
pub struct Entry {
    pub uid: String,
    pub salt: Vec<u8>,
    pub nonce: Vec<u8>,
    pub ciphertext: Vec<u8>,
}

#[derive(Debug, Clone)]
pub struct VaultFile {
    pub mode: VaultMode,
    pub backend: Option<String>,
    pub vault_id: Option<Vec<u8>>,
    pub created_at_ms: i64,
    pub alg: u8,
    pub slots: Vec<Slot>,
    pub entries: Vec<Entry>,
    pub trailer: Option<Vec<u8>>,
    pub mac_input: Option<Vec<u8>>,
}

/// 文件头元数据（非密文；serve/status 握手前读取模式/KDF）。
#[derive(Debug, Clone)]
pub struct VaultHeader {
    pub mode: VaultMode,
    pub backend: Option<String>,
    pub slot_kinds: Vec<SlotKind>,
    pub kdf: Option<KdfParams>,
}

/// 槽位 AD（域分离）= vaultId ‖ kind。
pub fn build_slot_ad(vault_id: &[u8], kind: SlotKind) -> Vec<u8> {
    let mut ad = Vec::with_capacity(vault_id.len() + 1);
    ad.extend_from_slice(vault_id);
    ad.push(kind.as_byte());
    ad
}

/// 由槽位集合推导因子模式。
pub fn detect_slots_mode(kinds: &[SlotKind]) -> SlotsMode {
    if kinds.len() == 1 && kinds[0] == SlotKind::Password {
        return SlotsMode::M1;
    }
    if kinds.iter().any(|k| *k == SlotKind::PasswordDevice) {
        return SlotsMode::M2a;
    }
    if kinds.iter().any(|k| *k == SlotKind::Device) {
        return SlotsMode::M2b;
    }
    SlotsMode::None
}

/// 尾部完整性 MAC = HMAC-SHA256(K, header‖entries)。
pub fn compute_slots_mac(master_key: &[u8], body: &[u8]) -> [u8; 32] {
    crypto::hmac_sha256(master_key, body)
}

// ── 读取游标 ────────────────────────────────────────────────────────

struct Cur<'a> {
    b: &'a [u8],
    pos: usize,
}

impl<'a> Cur<'a> {
    fn new(b: &'a [u8]) -> Self {
        Self { b, pos: 0 }
    }
    fn take(&mut self, n: usize) -> Result<&'a [u8]> {
        if self.pos + n > self.b.len() {
            return Err(VaultError::invalid_data("vault 文件头损坏（读取越界）"));
        }
        let s = &self.b[self.pos..self.pos + n];
        self.pos += n;
        Ok(s)
    }
    fn u8(&mut self) -> Result<u8> {
        Ok(self.take(1)?[0])
    }
    fn u16le(&mut self) -> Result<u16> {
        let s = self.take(2)?;
        Ok(u16::from_le_bytes([s[0], s[1]]))
    }
    fn u32le(&mut self) -> Result<u32> {
        let s = self.take(4)?;
        Ok(u32::from_le_bytes([s[0], s[1], s[2], s[3]]))
    }
    fn i64le(&mut self) -> Result<i64> {
        let s = self.take(8)?;
        Ok(i64::from_le_bytes([
            s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7],
        ]))
    }
}

fn read_backend(cur: &mut Cur) -> Result<Option<String>> {
    let len = cur.u8()? as usize;
    if len == 0 {
        return Ok(None);
    }
    let bytes = cur.take(len)?;
    Ok(Some(String::from_utf8_lossy(bytes).into_owned()))
}

fn read_kdf(cur: &mut Cur) -> Result<KdfParams> {
    let m = cur.u32le()?;
    let t = cur.u32le()?;
    let p = cur.u32le()?;
    validate_kdf(m, t, p)?;
    Ok(KdfParams {
        m_kib: m,
        t,
        p,
    })
}

fn read_slot(cur: &mut Cur) -> Result<Slot> {
    let kind = SlotKind::from_byte(cur.u8()?)?;
    let kdf = match kind {
        SlotKind::Password | SlotKind::PasswordDevice => read_kdf(cur)?,
        _ => KdfParams {
            m_kib: cur.u32le()?,
            t: cur.u32le()?,
            p: cur.u32le()?,
        },
    };
    let salt = cur.take(SALT_SIZE)?.to_vec();
    let nonce = cur.take(NONCE_SIZE)?.to_vec();
    let wrapped_len = cur.u16le()? as usize;
    if wrapped_len < KEY_SIZE {
        return Err(VaultError::invalid_data(
            "槽位封装长度异常（小于主密钥），拒绝加载",
        ));
    }
    let wrapped = cur.take(wrapped_len)?.to_vec();
    Ok(Slot {
        kind,
        kdf,
        salt,
        nonce,
        wrapped,
    })
}

fn read_entries(cur: &mut Cur, file: &mut VaultFile, raw_len: usize) -> Result<()> {
    let count = cur.u32le()?;
    if count > MAX_ENTRIES {
        return Err(VaultError::invalid_data(
            "vault 条目数异常（可能被恶意构造），拒绝加载",
        ));
    }
    for _ in 0..count {
        let uid_len = cur.u8()? as usize;
        let uid = String::from_utf8_lossy(cur.take(uid_len)?).into_owned();
        let salt = cur.take(SALT_SIZE)?.to_vec();
        let nonce = cur.take(NONCE_SIZE)?.to_vec();
        let ct_len = cur.u32le()?;
        if ct_len > MAX_ENTRY_BYTES || (ct_len as usize) > raw_len.saturating_sub(cur.pos) {
            return Err(VaultError::invalid_data(
                "vault 条目长度异常（可能被恶意构造），拒绝加载",
            ));
        }
        let ciphertext = cur.take(ct_len as usize)?.to_vec();
        file.entries.push(Entry {
            uid,
            salt,
            nonce,
            ciphertext,
        });
    }
    Ok(())
}

fn read_all(path: &str) -> Result<Vec<u8>> {
    // 拒绝符号链接 / 非普通文件（防「重定向读取」），并按上限**在读取前**判定长度
    // （防植入超大文件把内容读进内存 → OOM）。
    let raw = match crate::safefile::read_regular_limited(Path::new(path), MAX_FILE_SIZE)? {
        Some(v) => v,
        None => {
            return Err(VaultError::from(std::io::Error::new(
                std::io::ErrorKind::NotFound,
                format!("vault 文件不存在：{path}"),
            )))
        }
    };
    if raw.len() as u64 > MAX_FILE_SIZE {
        return Err(VaultError::invalid_data(
            "vault 文件过大（可能被恶意填充），拒绝加载",
        ));
    }
    Ok(raw)
}

/// 仅读文件头（serve 握手前判定模式用；不加载任何密文）。
pub fn read_header(path: &str) -> Result<VaultHeader> {
    let raw = read_all(path)?;
    let mut cur = Cur::new(&raw);
    if cur.u32le()? != MAGIC {
        return Err(VaultError::invalid_data("vault 文件头损坏（magic 不符）"));
    }
    match cur.u8()? {
        MODE_CRYPTO => {
            cur.take(3)?;
            cur.take(KEY_SIZE)?; // 零占位
            let backend = read_backend(&mut cur)?;
            Ok(VaultHeader {
                mode: VaultMode::Crypto,
                backend,
                slot_kinds: Vec::new(),
                kdf: None,
            })
        }
        MODE_SLOTS => {
            cur.take(VAULT_ID_SIZE)?;
            cur.take(8)?;
            cur.u8()?; // alg
            let slot_count = cur.u8()? as usize;
            let mut kinds = Vec::with_capacity(slot_count);
            let mut kdf = None;
            for _ in 0..slot_count {
                let slot = read_slot(&mut cur)?;
                kinds.push(slot.kind);
                if kdf.is_none()
                    && matches!(slot.kind, SlotKind::Password | SlotKind::PasswordDevice)
                {
                    kdf = Some(slot.kdf);
                }
            }
            Ok(VaultHeader {
                mode: VaultMode::Slots,
                backend: None,
                slot_kinds: kinds,
                kdf,
            })
        }
        _ => Err(VaultError::invalid_data("vault 版本不兼容")),
    }
}

/// 加载整份 vault 文件。
pub fn load(path: &str) -> Result<VaultFile> {
    let raw = read_all(path)?;
    let mut cur = Cur::new(&raw);
    if cur.u32le()? != MAGIC {
        return Err(VaultError::invalid_data("vault 文件头损坏（magic 不符）"));
    }
    let version = cur.u8()?;
    let mut file = match version {
        MODE_CRYPTO => {
            cur.take(3)?;
            let key_vault = cur.take(KEY_SIZE)?.to_vec();
            let backend = read_backend(&mut cur)?;
            let _ = key_vault;
            VaultFile {
                mode: VaultMode::Crypto,
                backend,
                vault_id: None,
                created_at_ms: 0,
                alg: 0,
                slots: Vec::new(),
                entries: Vec::new(),
                trailer: None,
                mac_input: None,
            }
        }
        MODE_SLOTS => {
            let vault_id = cur.take(VAULT_ID_SIZE)?.to_vec();
            let created_at_ms = cur.i64le()?;
            let alg = cur.u8()?;
            let slot_count = cur.u8()? as usize;
            let mut slots = Vec::with_capacity(slot_count);
            for _ in 0..slot_count {
                slots.push(read_slot(&mut cur)?);
            }
            VaultFile {
                mode: VaultMode::Slots,
                backend: None,
                vault_id: Some(vault_id),
                created_at_ms,
                alg,
                slots,
                entries: Vec::new(),
                trailer: None,
                mac_input: None,
            }
        }
        _ => return Err(VaultError::invalid_data("vault 版本不兼容")),
    };
    read_entries(&mut cur, &mut file, raw.len())?;
    if file.mode == VaultMode::Slots {
        if raw.len() < TRAILER_SIZE {
            return Err(VaultError::invalid_data(
                "slots vault 缺少尾部 HMAC，拒绝加载",
            ));
        }
        file.trailer = Some(raw[raw.len() - TRAILER_SIZE..].to_vec());
        file.mac_input = Some(raw[..raw.len() - TRAILER_SIZE].to_vec());
    }
    Ok(file)
}

// ── 写入 ────────────────────────────────────────────────────────────

fn write_slot(out: &mut Vec<u8>, slot: &Slot) -> Result<()> {
    out.push(slot.kind.as_byte());
    out.extend_from_slice(&slot.kdf.m_kib.to_le_bytes());
    out.extend_from_slice(&slot.kdf.t.to_le_bytes());
    out.extend_from_slice(&slot.kdf.p.to_le_bytes());
    out.extend_from_slice(&slot.salt);
    out.extend_from_slice(&slot.nonce);
    if slot.wrapped.len() > u16::MAX as usize {
        return Err(VaultError::invalid_data("槽位封装过长"));
    }
    out.extend_from_slice(&(slot.wrapped.len() as u16).to_le_bytes());
    out.extend_from_slice(&slot.wrapped);
    Ok(())
}

fn write_body_core(file: &VaultFile) -> Result<Vec<u8>> {
    let mut out = Vec::new();
    out.extend_from_slice(&MAGIC.to_le_bytes());
    match file.mode {
        VaultMode::Slots => {
            out.push(MODE_SLOTS);
            let vid = file
                .vault_id
                .as_ref()
                .ok_or_else(|| VaultError::invalid_data("slots vault 缺少 vaultId"))?;
            out.extend_from_slice(vid);
            out.extend_from_slice(&file.created_at_ms.to_le_bytes());
            out.push(file.alg);
            out.push(file.slots.len() as u8);
            for slot in &file.slots {
                write_slot(&mut out, slot)?;
            }
        }
        VaultMode::Crypto => {
            out.push(MODE_CRYPTO);
            out.extend_from_slice(&[0u8; 3]);
            out.extend_from_slice(&[0u8; KEY_SIZE]); // key_vault 零占位
            let backend = file.backend.clone().unwrap_or_default();
            let bytes = backend.as_bytes();
            if bytes.len() > u8::MAX as usize {
                return Err(VaultError::invalid_data("后端指纹过长"));
            }
            out.push(bytes.len() as u8);
            out.extend_from_slice(bytes);
        }
    }
    out.extend_from_slice(&(file.entries.len() as u32).to_le_bytes());
    for e in &file.entries {
        let uid = e.uid.as_bytes();
        out.push(uid.len() as u8);
        out.extend_from_slice(uid);
        out.extend_from_slice(&e.salt);
        out.extend_from_slice(&e.nonce);
        out.extend_from_slice(&(e.ciphertext.len() as u32).to_le_bytes());
        out.extend_from_slice(&e.ciphertext);
    }
    Ok(out)
}

/// 保存（原子写 + `.bak` 备份）。`master_key` 仅 slots 模式使用（尾部 HMAC）。
///
/// 走 [crate::safefile::write_atomic]：tmp 独占创建（不跟随符号链接）、FIFO/符号链接
/// 一律拒绝；`.bak` 亦为原子写（旧实现用 `fs::copy` 会跟随目标符号链接写到任意文件）。
pub fn save(path: &str, file: &VaultFile, master_key: Option<&[u8]>) -> Result<()> {
    let p = Path::new(path);
    let body = write_body_core(file)?;
    let mut content = body.clone();
    if file.mode == VaultMode::Slots {
        let mk = master_key.ok_or_else(|| {
            VaultError::invalid_data("slots 模式写入缺少主密钥（无法计算尾部 HMAC）")
        })?;
        if mk.len() != KEY_SIZE {
            return Err(VaultError::invalid_data(
                "slots 模式写入缺少主密钥（无法计算尾部 HMAC）",
            ));
        }
        let mac = compute_slots_mac(mk, &body);
        content.extend_from_slice(&mac);
    }

    // 整库容量校验（写入侧）：超过 [MAX_FILE_SIZE] 会使库**此后无法加载**（同上，
    // 属可用性自毁），故在落盘前拒绝，磁盘保持原样。
    if content.len() as u64 > MAX_FILE_SIZE {
        return Err(VaultError::argument(format!(
            "凭据库已达容量上限（{} B，含尾部校验）：本次写入 {} B 会被拒绝；请删除不再需要的条目",
            MAX_FILE_SIZE,
            content.len()
        )));
    }
    // 保留上一版为 .bak（best-effort：备份失败不阻断保存）。
    if p.exists() {
        let _ = crate::safefile::backup_best_effort(p);
    }
    crate::safefile::write_atomic(p, &content, false)
}

/// 加密单条凭据（独立 salt/nonce，AES-256-GCM，无 AD）。
///
/// **写入侧容量校验**：单条密文（明文 + 16B 认证标签）不得超过 [MAX_ENTRY_BYTES]。
/// 加载侧同样有该上限；若只在加载侧校验，一次超限写入会「成功」却让库此后**无法加载**
/// （fail-closed 自锁，只能靠 `.bak` 救）——实测 4096 KiB 明文即可击穿。故此处**在落盘前**
/// 拒绝，返回明确错误且**不改动磁盘**。
pub fn encrypt_entry(uid: &str, plaintext: &[u8], master_key: &[u8]) -> Result<Entry> {
    if plaintext.len() + TAG_SIZE > MAX_ENTRY_BYTES as usize {
        return Err(VaultError::argument(format!(
            "单条凭据过大：密文上限 {} B（明文上限 {} B，含 {} B GCM 认证标签），当前明文 {} B",
            MAX_ENTRY_BYTES,
            MAX_ENTRY_BYTES as usize - TAG_SIZE,
            TAG_SIZE,
            plaintext.len()
        )));
    }
    let salt = crypto::random_vec(SALT_SIZE)?;
    let nonce = crypto::random_vec(NONCE_SIZE)?;
    let ciphertext = crypto::aes256gcm_encrypt(master_key, &nonce, plaintext, &[])?;
    Ok(Entry {
        uid: uid.to_string(),
        salt,
        nonce,
        ciphertext,
    })
}

/// 解密单条凭据；GCM 认证失败抛 [VaultError::AuthTag]。
pub fn decrypt_entry(entry: &Entry, master_key: &[u8]) -> Result<Vec<u8>> {
    crypto::aes256gcm_decrypt(master_key, &entry.nonce, &entry.ciphertext, &[])
}

/// 用 [kek] 封装主密钥 [master] 为槽位（AD = vaultId‖kind）。
pub fn wrap_slot(
    kind: SlotKind,
    kdf: Option<KdfParams>,
    salt: Vec<u8>,
    kek: &[u8],
    master: &[u8],
    ad: &[u8],
) -> Result<Slot> {
    let nonce = crypto::random_vec(NONCE_SIZE)?;
    let wrapped = crypto::aes256gcm_encrypt(kek, &nonce, master, ad)?;
    Ok(Slot {
        kind,
        kdf: kdf.unwrap_or(KdfParams {
            m_kib: 0,
            t: 0,
            p: 0,
        }),
        salt,
        nonce,
        wrapped,
    })
}
