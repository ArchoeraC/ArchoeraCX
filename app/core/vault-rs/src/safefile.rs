// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 敏感文件落盘原语（vault 文件 / `secret.key` / `device.key` / `sshare.bin` /
//! `vault.lockout` 共用）。
//!
//! 为什么不能直接用 `fs::write` / `fs::copy` / `File::create`：
//!   - **符号链接写入穿透**：`File::create` 跟随符号链接 → 攻击者把
//!     `<file>.tmp` / `<file>.bak` 预置为符号链接，即可让 vault 覆盖**任意用户文件**
//!     （实测：`.tmp`/`.bak` 均可覆盖 canary）；且 `File::create`+`rename` 会把
//!     符号链接本身搬成 vault 路径 → 后续读写被**重定向**到攻击者指定文件；
//!   - **FIFO 挂死**：`File::create(tmp)` 对已存在的 FIFO 阻塞在 `open(O_WRONLY)`，
//!     保存路径**无超时** → vault 永久挂死（会话与主密钥驻留无上界）。
//!
//! 因此统一约定：
//!   - 读：必须是**普通文件**（`O_NOFOLLOW` + `lstat` 判定），符号链接/FIFO/目录一律拒绝；
//!   - 写：tmp 以 `O_CREAT|O_EXCL`（不跟随符号链接）创建；已存在的 tmp 仅当是**普通文件**
//!     （崩溃残留）才清理重建，否则拒绝；最后 `rename` 原子替换目标（rename 不跟随符号链接，
//!     故目标即使被预置为符号链接也只替换该链接本身，不会写到链接指向的文件）。

use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};

use crate::errors::{Result, VaultError};

/// 临时文件路径：`<path>.tmp.<pid>`（**进程唯一**）。
///
/// 为什么带 pid：`<path>.tmp` 是**可预测的共享名**，多会话并发保存时会互相踩——
/// 实测 4 路同时保存 40 次写入有 2 次失败（一方「清理陈旧 tmp」删掉另一方正在用的
/// tmp → 对方 `rename` ENOENT / 自己 `create_new` EEXIST）。带 pid 后各进程各写各的，
/// 只保留「拒绝符号链接/FIFO」与「同名陈旧普通文件清理」两种语义。
fn tmp_path(path: &Path) -> PathBuf {
    PathBuf::from(format!("{}.tmp.{}", path.display(), std::process::id()))
}

/// 旧版本遗留的可预测临时名（`<path>.tmp`）：仅当它是**普通文件**时清理（升级卫生），
/// 符号链接 / FIFO / 设备一律不动（那是攻击面对抗，不是残留）。
fn clean_legacy_tmp(path: &Path) {
    let legacy = PathBuf::from(format!("{}.tmp", path.display()));
    if let Ok(md) = fs::symlink_metadata(&legacy) {
        if md.file_type().is_file() {
            let _ = fs::remove_file(&legacy);
        }
    }
}

/// 读取**普通文件**内容（不限长）：缺失 → `Ok(None)`；符号链接 / FIFO / 设备 / 目录 → 拒绝。
///
/// 注意：无上限读取会被「植入一个超大普通文件」打成 OOM，敏感状态文件请用
/// [`read_regular_limited`]。
pub fn read_regular(path: &Path) -> Result<Option<Vec<u8>>> {
    read_regular_limited(path, u64::MAX)
}

/// 读取**普通文件**内容并限制长度：缺失 → `Ok(None)`；非普通文件 → 拒绝；
/// 超过 [max_len] → 拒绝（**读之前**按元数据判定，避免把巨量内容读进内存）。
pub fn read_regular_limited(path: &Path, max_len: u64) -> Result<Option<Vec<u8>>> {
    let md = match fs::symlink_metadata(path) {
        Ok(md) => md,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(e) => return Err(VaultError::from(e)),
    };
    if md.file_type().is_symlink() {
        return Err(VaultError::invalid_operation(format!(
            "拒绝读取：{} 是符号链接（可能被重定向到其它文件）",
            path.display()
        )));
    }
    if !md.file_type().is_file() {
        return Err(VaultError::invalid_operation(format!(
            "拒绝读取：{} 不是普通文件",
            path.display()
        )));
    }
    if md.len() > max_len {
        return Err(VaultError::invalid_data(format!(
            "拒绝读取：{} 长度 {} 超过上限 {}",
            path.display(),
            md.len(),
            max_len
        )));
    }
    // O_NOFOLLOW：关闭「lstat 与 open 之间被换成符号链接」的竞态窗口。
    let f = open_read_nofollow(path)?;
    // 读取量同样带上限（多读 1 字节用于判定超限）：关闭「lstat 之后文件被增长」的 TOCTOU
    // ——否则植入普通文件后立刻膨胀仍可把巨量内容读进内存。
    let mut buf = Vec::with_capacity(md.len() as usize);
    {
        let mut limited = std::io::Read::take(f, max_len.saturating_add(1));
        std::io::Read::read_to_end(&mut limited, &mut buf)?;
    }
    if buf.len() as u64 > max_len {
        return Err(VaultError::invalid_data(format!(
            "拒绝读取：{} 内容超过上限 {}（检查后被增长）",
            path.display(),
            max_len
        )));
    }
    Ok(Some(buf))
}

#[cfg(unix)]
fn open_read_nofollow(path: &Path) -> std::io::Result<fs::File> {
    use std::os::unix::fs::OpenOptionsExt;
    fs::OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_NOFOLLOW)
        .open(path)
}

#[cfg(not(unix))]
fn open_read_nofollow(path: &Path) -> std::io::Result<fs::File> {
    // Windows：创建符号链接/junction 需特权或开发者模式；此处以 lstat 判定为准
    // （上方 read_regular 已拒绝 reparse point：Rust 将 junction/symlink 均报告为 symlink）。
    fs::File::open(path)
}

/// 仅当 tmp 是「崩溃残留的普通文件」时清理；符号链接 / FIFO / 设备 / 目录一律拒绝。
fn prepare_tmp(tmp: &Path) -> Result<()> {
    match fs::symlink_metadata(tmp) {
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(VaultError::from(e)),
        Ok(md) if md.file_type().is_file() => {
            fs::remove_file(tmp).map_err(VaultError::from)
        }
        Ok(_) => Err(VaultError::invalid_operation(format!(
            "拒绝写入：临时文件路径已存在且非普通文件（符号链接 / FIFO / 设备 / 目录）：{}",
            tmp.display()
        ))),
    }
}

fn create_new_tmp(tmp: &Path, private: bool) -> std::io::Result<fs::File> {
    let mut opts = fs::OpenOptions::new();
    opts.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        if private {
            opts.mode(0o600);
        }
        let _ = private;
    }
    #[cfg(not(unix))]
    let _ = private;
    opts.open(tmp)
}

/// 原子写入：tmp（独占创建）→ 落盘 → 可选 0600 → `rename` 覆盖目标。
///
/// `private = true` 时目标为 0600（Unix）；Windows 沿用目录 ACL（与既往行为一致）。
pub fn write_atomic(path: &Path, data: &[u8], private: bool) -> Result<()> {
    if let Some(dir) = path.parent() {
        if !dir.as_os_str().is_empty() {
            fs::create_dir_all(dir)?;
        }
    }
    let tmp = tmp_path(path);
    prepare_tmp(&tmp)?;
    let mut f = match create_new_tmp(&tmp, private) {
        Ok(f) => f,
        Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {
            // 竞态：prepare 之后被抢占创建。再次判定后重试一次。
            prepare_tmp(&tmp)?;
            create_new_tmp(&tmp, private).map_err(VaultError::from)?
        }
        Err(e) => return Err(VaultError::from(e)),
    };
    let write_result = (|| -> Result<()> {
        f.write_all(data)?;
        f.flush()?;
        f.sync_all()?;
        Ok(())
    })();
    if let Err(e) = write_result {
        drop(f);
        let _ = fs::remove_file(&tmp); // 不留半成品
        return Err(e);
    }
    drop(f);
    #[cfg(unix)]
    if private {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(&tmp, fs::Permissions::from_mode(0o600))?;
    }
    fs::rename(&tmp, path)?;
    clean_legacy_tmp(path);
    Ok(())
}

/// 备份当前 `path` 为 `path + ".bak"`（同为原子写，失败不阻断调用方）。
///
/// 与既往 `fs::copy` 的差别：`fs::copy` 会**跟随**目标符号链接写入任意文件；
/// 此处走 `write_atomic`（tmp 独占 + rename），符号链接只被替换、不被穿透。
pub fn backup_best_effort(path: &Path) -> Result<()> {
    let current = match read_regular(path)? {
        Some(v) => v,
        None => return Ok(()),
    };
    let bak = PathBuf::from(format!("{}.bak", path.display()));
    write_atomic(&bak, &current, false)
}

// ── 数据目录写锁（跨进程互斥）────────────────────────────────────────

/// 锁文件名（**永不删除**：删除会让持锁进程与新进程各锁不同 inode，互斥失效）。
/// 内容为空，非敏感面。
pub const LOCK_NAME: &str = "vault.lock";

/// 数据目录的跨进程**写锁**（跨进程互斥的「读-改-写」保护）。
///
/// 为什么需要：`set` / `delete` / `set-kdf` / `init` / `destroy` 都是「加载 → 改 → 保存」，
/// 多会话并发时两个进程可能基于同一份旧状态各自保存 → **静默丢更新**（后写者覆盖前者）。
/// 之前只能靠「最后写者获胜」，属不可接受的静默数据丢失。
///
/// 语义：
///   - **阻塞式独占**（`flock(LOCK_EX)` / `LockFileEx` 不带 FAIL_IMMEDIATELY）：拿不到就在内核里等，
///     由解锁事件唤醒——**不是轮询**；`EINTR` 重试是信号中断重试，非忙等；
///   - **崩溃自动释放**：锁由内核随进程退出释放，无陈旧锁；
///   - 锁范围是**单条命令**（不是整场会话）：多会话仍可交错执行，只是每条命令的
///     「读-改-写」原子化；
///   - 锁文件创建走安全打开（`O_NOFOLLOW|O_NONBLOCK` + `fstat` 验普通文件），
///     被植入符号链接 / FIFO / 设备 → 拒绝（与其余敏感文件同一防线）。
pub struct DataDirLock {
    file: std::fs::File,
}

impl DataDirLock {
    /// 获取数据目录写锁（必要时创建数据目录与锁文件）。
    pub fn acquire(data_dir: &str) -> Result<Self> {
        let path = Path::new(data_dir).join(LOCK_NAME);
        if let Some(dir) = path.parent() {
            if !dir.as_os_str().is_empty() {
                fs::create_dir_all(dir)?;
            }
        }
        let file = open_lock_file(&path)?;
        lock_exclusive(&file)?;
        Ok(Self { file })
    }
}

impl Drop for DataDirLock {
    fn drop(&mut self) {
        unlock(&self.file);
        // 句柄随结构体关闭；锁文件保留（见 LOCK_NAME 说明）。
    }
}

#[cfg(unix)]
fn open_lock_file(path: &Path) -> Result<std::fs::File> {
    use std::os::unix::fs::OpenOptionsExt;
    // O_NONBLOCK：万一被植入 FIFO，open 也立即返回（不阻塞在打开阶段）。
    let f = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .mode(0o600)
        .custom_flags(libc::O_NOFOLLOW | libc::O_CLOEXEC | libc::O_NONBLOCK)
        .open(path)?;
    let md = f.metadata()?;
    if !md.is_file() {
        return Err(VaultError::invalid_operation(format!(
            "拒绝加锁：{} 不是普通文件（符号链接 / FIFO / 设备）",
            path.display()
        )));
    }
    Ok(f)
}

#[cfg(unix)]
fn lock_exclusive(f: &std::fs::File) -> Result<()> {
    use std::os::unix::io::AsRawFd;
    loop {
        if unsafe { libc::flock(f.as_raw_fd(), libc::LOCK_EX) } == 0 {
            return Ok(());
        }
        let err = std::io::Error::last_os_error();
        // 被信号中断 → 重试（信号中断重试，非忙等）
        if err.raw_os_error() == Some(libc::EINTR) {
            continue;
        }
        return Err(VaultError::from(err));
    }
}

#[cfg(unix)]
fn unlock(f: &std::fs::File) {
    use std::os::unix::io::AsRawFd;
    unsafe {
        libc::flock(f.as_raw_fd(), libc::LOCK_UN);
    }
}

#[cfg(windows)]
fn open_lock_file(path: &Path) -> Result<std::fs::File> {
    // std 默认以 FILE_SHARE_READ|WRITE|DELETE 打开 → 别的进程也能打开并加锁（Win32 锁是强制的）。
    let f = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(path)?;
    let md = f.metadata()?;
    if !md.is_file() {
        return Err(VaultError::invalid_operation(format!(
            "拒绝加锁：{} 不是普通文件",
            path.display()
        )));
    }
    Ok(f)
}

#[cfg(windows)]
fn lock_exclusive(f: &std::fs::File) -> Result<()> {
    use std::os::windows::io::AsRawHandle;
    use windows::Win32::Foundation::HANDLE;
    use windows::Win32::Storage::FileSystem::{LockFileEx, LOCKFILE_EXCLUSIVE_LOCK};
    use windows::Win32::System::IO::OVERLAPPED;
    let mut ov = OVERLAPPED::default();
    // 不带 LOCKFILE_FAIL_IMMEDIATELY → 阻塞等待（事件唤醒，非轮询）；锁首字节。
    unsafe {
        LockFileEx(
            HANDLE(f.as_raw_handle() as *mut core::ffi::c_void),
            LOCKFILE_EXCLUSIVE_LOCK,
            0,
            1,
            0,
            &mut ov,
        )
        .map_err(|e| {
            VaultError::invalid_operation(format!("加锁失败（Win32={}）", e.code().0))
        })?;
    }
    Ok(())
}

#[cfg(windows)]
fn unlock(f: &std::fs::File) {
    use std::os::windows::io::AsRawHandle;
    use windows::Win32::Foundation::HANDLE;
    use windows::Win32::Storage::FileSystem::UnlockFile;
    // 与 LockFileEx（偏移 0、长度 1 字节）对应的解锁：同范围即可，无需 OVERLAPPED。
    unsafe {
        let _ = UnlockFile(
            HANDLE(f.as_raw_handle() as *mut core::ffi::c_void),
            0,
            0,
            1,
            0,
        );
    }
}

#[cfg(not(any(unix, windows)))]
fn open_lock_file(path: &Path) -> Result<std::fs::File> {
    Ok(fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(path)?)
}

#[cfg(not(any(unix, windows)))]
fn lock_exclusive(_f: &std::fs::File) -> Result<()> {
    Ok(())
}

#[cfg(not(any(unix, windows)))]
fn unlock(_f: &std::fs::File) {}
