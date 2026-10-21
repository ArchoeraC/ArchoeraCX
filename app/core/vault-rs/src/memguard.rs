// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 内存护栏：主密钥 / 短时明文缓冲的**禁换页 + 禁转储 + 用后零化**。
//!
//! 对照 C# `MemGuard.LockedBuffer`（memlock 语义一致），三端各自使用 OS 原语：
//!   - Linux/Unix：`mlock`/`munlock`；Linux 另加 `madvise(MADV_DONTDUMP)`（禁进 core）；
//!   - Windows：`VirtualLock`/`VirtualUnlock`（先尝试提高进程工作集下界）；
//!   - 释放：`zeroize` 后解锁（先零化再 munlock，避免解锁后被换出）。
//!
//! **每缓冲独占页**（关键）：「两个小缓冲落在同一页」时，其中一个释放触发
//! `munlock(整页)` 会把另一个仍在使用中的缓冲**一并解锁**（实测：会话中 VmLck 归零）。
//! 故此处按页对齐分配（`align = size = 向上取整到页`），保证任一缓冲的解锁只影响自己。
//!
//! **降级策略（如实）**：locking 可能因 `RLIMIT_MEMLOCK` / 工作集限制而失败。
//! 构造时**尝试**提升软限与工作集下界（[`raise_lock_quota`]），仍失败则记一次 stderr
//! 告警并降级为「仅 zeroize」——不 fail-closed 自砖；锁定状态可用
//! [`LockedBuffer::is_locked`]（或 `archoera-vault selftest-mem`）查询。

use std::alloc::{alloc_zeroed, dealloc, handle_alloc_error, Layout};
use std::ptr::NonNull;

use zeroize::Zeroize;

/// 进程级一次性提示：锁定降级只在首次告警。
static WARNED: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// 页大小（Unix `sysconf(_SC_PAGESIZE)`；Windows 固定 4 KiB）。
fn page_size() -> usize {
    #[cfg(unix)]
    {
        let v = unsafe { libc::sysconf(libc::_SC_PAGESIZE) };
        if v > 0 {
            return v as usize;
        }
    }
    4096
}

fn round_up_to_page(n: usize) -> usize {
    let p = page_size();
    n.max(1).div_ceil(p) * p
}

/// 页对齐、已锁定（或降级后未锁定）的敏感字节缓冲。
///
/// 逻辑长度（[`LockedBuffer::len`]）与分配长度可能不同：分配长度是向上取整到页，
/// 以保证独占页；零化与解锁都作用于整块分配区。
pub struct LockedBuffer {
    ptr: NonNull<u8>,
    len: usize,
    layout: Layout,
    locked: bool,
}

impl LockedBuffer {
    /// 分配并清零 [size] 字节，随后尝试锁定。
    pub fn new(size: usize) -> Self {
        let layout = Layout::from_size_align(round_up_to_page(size), page_size())
            .expect("页对齐布局合法");
        // SAFETY: layout 由合法 size/align 构造；失败即 abort（与 Vec 分配失败一致）。
        let raw = unsafe { alloc_zeroed(layout) };
        let ptr = NonNull::new(raw).unwrap_or_else(|| handle_alloc_error(layout));
        let locked = lock_region(ptr.as_ptr(), layout.size());
        Self {
            ptr,
            len: size,
            layout,
            locked,
        }
    }

    /// 从字节复制构造（源缓冲由调用方自行零化；见 [`LockedBuffer::take`]）。
    pub fn from_bytes(src: &[u8]) -> Self {
        let buf = Self::new(src.len());
        // SAFETY: buf 分配长度 ≥ src.len()。
        unsafe { std::ptr::copy_nonoverlapping(src.as_ptr(), buf.ptr.as_ptr(), src.len()) };
        buf
    }

    /// 接管一个 `Vec`：复制进锁定缓冲后**立即零化源缓冲**。
    /// 关键派生路径（`argon2id` / `hkdf` / AES 解密输出）一律经此收口。
    pub fn take(mut src: Vec<u8>) -> Self {
        let buf = Self::from_bytes(&src);
        src.zeroize();
        buf
    }

    /// 是否真的锁进了物理内存（降级时为 false）。
    pub fn is_locked(&self) -> bool {
        self.locked
    }

    pub fn len(&self) -> usize {
        self.len
    }

    pub fn as_slice(&self) -> &[u8] {
        // SAFETY: ptr 指向 len 个已初始化字节，生命周期受 &self 约束。
        unsafe { std::slice::from_raw_parts(self.ptr.as_ptr(), self.len) }
    }
}

impl Drop for LockedBuffer {
    fn drop(&mut self) {
        // 先零化整块分配区（含对齐填充），再解锁：解锁后该页可被换出。
        // SAFETY: ptr/layout 为本结构分配所得。
        unsafe {
            let region = std::slice::from_raw_parts_mut(self.ptr.as_ptr(), self.layout.size());
            region.zeroize();
        }
        if self.locked {
            unlock_region(self.ptr.as_ptr(), self.layout.size());
        }
        // SAFETY: 与分配时同一 layout。
        unsafe { dealloc(self.ptr.as_ptr(), self.layout) };
    }
}

impl std::fmt::Debug for LockedBuffer {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        // 绝不打印内容。
        write!(f, "LockedBuffer(len={}, locked={})", self.len, self.locked)
    }
}

/// 尝试锁定一段区域；返回是否成功（失败记一次告警）。
fn lock_region(addr: *mut u8, len: usize) -> bool {
    if len == 0 {
        return false;
    }
    if platform_lock(addr, len) {
        platform_no_dump(addr, len);
        true
    } else {
        warn_once();
        false
    }
}

fn warn_once() {
    use std::sync::atomic::Ordering;
    if !WARNED.swap(true, Ordering::SeqCst) {
        eprintln!(
            "[vault] 内存锁定失败（RLIMIT_MEMLOCK / 工作集限制）：已降级为「仅 zeroize」；\
             建议提高锁定额度（见 docs/vault-security-notes.md）"
        );
    }
}

// ── Unix（Linux/macOS）：mlock / munlock ───────────────────────────

#[cfg(unix)]
fn platform_lock(addr: *mut u8, len: usize) -> bool {
    unsafe { libc::mlock(addr as *const libc::c_void, len) == 0 }
}

#[cfg(unix)]
fn unlock_region(addr: *mut u8, len: usize) {
    unsafe {
        libc::munlock(addr as *const libc::c_void, len);
    }
}

/// Linux：`MADV_DONTDUMP` 使该区域不进 core（即使 RLIMIT_CORE 被外部提高）。
#[cfg(target_os = "linux")]
fn platform_no_dump(addr: *mut u8, len: usize) {
    unsafe {
        libc::madvise(addr as *mut libc::c_void, len, libc::MADV_DONTDUMP);
    }
}

/// 非 Linux 的 Unix（macOS 无 MADV_DONTDUMP）：`RLIMIT_CORE=0` 已由 guard 兜底。
#[cfg(all(unix, not(target_os = "linux")))]
fn platform_no_dump(_addr: *mut u8, _len: usize) {}

/// 提升 `RLIMIT_MEMLOCK` 软限至硬限（不越权；硬限由管理员/容器设定）。
#[cfg(unix)]
pub fn raise_lock_quota() {
    unsafe {
        let mut rl = libc::rlimit {
            rlim_cur: 0,
            rlim_max: 0,
        };
        if libc::getrlimit(libc::RLIMIT_MEMLOCK, &mut rl) == 0 && rl.rlim_cur < rl.rlim_max {
            rl.rlim_cur = rl.rlim_max;
            let _ = libc::setrlimit(libc::RLIMIT_MEMLOCK, &rl);
        }
    }
}

// ── Windows：VirtualLock / VirtualUnlock ───────────────────────────

#[cfg(windows)]
fn platform_lock(addr: *mut u8, len: usize) -> bool {
    use windows::Win32::System::Memory::VirtualLock;
    unsafe { VirtualLock(addr as *const core::ffi::c_void, len).is_ok() }
}

#[cfg(windows)]
fn unlock_region(addr: *mut u8, len: usize) {
    use windows::Win32::System::Memory::VirtualUnlock;
    unsafe {
        let _ = VirtualUnlock(addr as *const core::ffi::c_void, len);
    }
}

/// Windows 无 MADV_DONTDUMP 等价物（转储由 WER 控制）；锁页已避免换出。
#[cfg(windows)]
fn platform_no_dump(_addr: *mut u8, _len: usize) {}

/// 提高进程工作集下界，让 `VirtualLock` 更容易成功（失败即忽略，best-effort）。
#[cfg(windows)]
pub fn raise_lock_quota() {
    use windows::Win32::System::Threading::{GetCurrentProcess, SetProcessWorkingSetSize};
    const TARGET_MIN: usize = 16 * 1024 * 1024;
    unsafe {
        let _ = SetProcessWorkingSetSize(GetCurrentProcess(), TARGET_MIN, usize::MAX);
    }
}

// ── 其它平台（无 OS 原语）：仅 zeroize 降级 ────────────────────────

#[cfg(not(any(unix, windows)))]
fn platform_lock(_addr: *mut u8, _len: usize) -> bool {
    false
}

#[cfg(not(any(unix, windows)))]
fn unlock_region(_addr: *mut u8, _len: usize) {}

#[cfg(not(any(unix, windows)))]
fn platform_no_dump(_addr: *mut u8, _len: usize) {}

#[cfg(not(any(unix, windows)))]
pub fn raise_lock_quota() {}
