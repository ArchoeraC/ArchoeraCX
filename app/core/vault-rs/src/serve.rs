// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 按需会话（与 C# ServeSession.cs 协议一致）：血缘校验 → 握手 → 命令循环 → 自退。

use zeroize::{Zeroize, Zeroizing};

use crate::encoding::{b64_decode, b64_encode};
use crate::errors::{Result, VaultError};
use crate::format::SlotsMode;
use crate::guard;
use crate::memguard::LockedBuffer;
use crate::service::{VaultService, AUTH_UID};
use crate::MARKER;

pub const HANDSHAKE_TIMEOUT_SECONDS: i32 = 10;
pub const IDLE_TIMEOUT_SECONDS: i32 = 30;

/// 单次读超时 / IO 错误。
pub enum ReadErr {
    Timeout,
    Io,
}

/// 行缓冲读取器（fd 上阻塞读 + 单次超时）。
///
/// 超时语义**不引入轮询**：每轮迭代都阻塞在 `poll(2)` 上由内核挂起，仅在
/// 「数据到达」事件或「单一截止时刻」到期时被唤醒；`loop` 只用于把已到达的字节
/// 拼到完整一行（无自旋、无周期性唤醒、无 CPU 空转）。
#[cfg(unix)]
pub struct LineReader {
    fd: i32,
    buf: Vec<u8>,
    start: usize,
}

#[cfg(unix)]
impl LineReader {
    pub fn new(fd: i32) -> Self {
        Self {
            fd,
            buf: Vec::new(),
            start: 0,
        }
    }

    /// 读取一行（剥离行尾 `\r\n`）；EOF 返回 Ok(None)；超时返回 Err(Timeout)。
    pub fn read_line(&mut self, timeout_ms: i32) -> std::result::Result<Option<String>, ReadErr> {
        loop {
            if let Some(idx) = self.buf[self.start..].iter().position(|&b| b == b'\n') {
                let end = self.start + idx;
                // 行字节（可能含口令/明文负载）先零化再返回解码结果。
                let decoded = {
                    let mut line = self.buf[self.start..end].to_vec();
                    let d = decode_line(&line);
                    line.zeroize();
                    d
                };
                self.start = end + 1;
                self.compact();
                return Ok(Some(decoded));
            }
            let mut pfd = libc::pollfd {
                fd: self.fd,
                events: libc::POLLIN,
                revents: 0,
            };
            let ready = unsafe { libc::poll(&mut pfd, 1, timeout_ms) };
            if ready == 0 {
                return Err(ReadErr::Timeout);
            }
            if ready < 0 {
                return Err(ReadErr::Io);
            }
            let mut tmp = [0u8; 4096];
            let n = unsafe { libc::read(self.fd, tmp.as_mut_ptr() as *mut libc::c_void, tmp.len()) };
            if n < 0 {
                return Err(ReadErr::Io);
            }
            if n == 0 {
                if self.start < self.buf.len() {
                    let decoded = {
                        let mut line = self.buf[self.start..].to_vec();
                        let d = decode_line(&line);
                        line.zeroize();
                        d
                    };
                    self.start = self.buf.len();
                    self.compact();
                    return Ok(Some(decoded));
                }
                return Ok(None);
            }
            self.buf.extend_from_slice(&tmp[..n as usize]);
        }
    }

    fn compact(&mut self) {
        // 已消费区间可能含口令/明文负载：丢弃前零化（缓冲区本身仍会被后续复用）。
        if self.start > 0 {
            self.buf[..self.start].zeroize();
        }
        if self.start > 0 && self.start == self.buf.len() {
            self.buf.clear();
            self.start = 0;
        } else if self.start > 4096 {
            self.buf.drain(0..self.start);
            self.start = 0;
        }
    }
}

/// 行尾 `\r\n` 剥离 + 宽松 UTF-8 解码（unix / Windows 两分支共用，语义一致）。
fn decode_line(line: &[u8]) -> String {
    let mut l = line;
    while l.last() == Some(&b'\n') || l.last() == Some(&b'\r') {
        l = &l[..l.len() - 1];
    }
    String::from_utf8_lossy(l).into_owned()
}

/// 非 unix（Windows）实现：专用读线程 + 事件唤醒的定时等待。
///
/// 语义与 C# `Console.In.ReadLineAsync().WaitAsync(seconds)` 对齐（C# 内部同样是
/// 「工作线程阻塞读 + 定时等待」，Windows 匿名管道不支持 overlapped I/O，无法用
/// `WaitForSingleObject` 直接等 stdin）：读线程在**原始句柄**上阻塞 `ReadFile`，主线程在
/// 通道上按**单一截止时刻**等待——仅由「行到达」事件或「截止时刻到期」唤醒。
/// **不引入轮询**：无自旋、无周期唤醒、无 CPU 空转（空闲实测 0.00% 单核）。
///
/// 为何不用 `std::io::stdin()`：`StdinLock` 内部持有**不受我们控制的 8 KiB 行缓冲**，
/// 读过的口令/明文负载会滞留在那里（实测内存扫描可命中）；因此改为 `ReadFile` 读进
/// **自有缓冲**，逐行切分后立即零化（与非 unix 侧一致的「原始读 + 自管缓冲」模型）。
#[cfg(not(unix))]
pub struct LineReader {
    rx: std::sync::mpsc::Receiver<LineMsg>,
}

#[cfg(not(unix))]
enum LineMsg {
    Line(String),
    Eof,
    Err,
}

/// 原始 stdin 句柄阻塞读（返回读到的字节数；0 = EOF）。
#[cfg(windows)]
fn read_stdin_raw(buf: &mut [u8]) -> std::io::Result<usize> {
    use windows::Win32::Storage::FileSystem::ReadFile;
    use windows::Win32::System::Console::{GetStdHandle, STD_INPUT_HANDLE};
    let handle = unsafe { GetStdHandle(STD_INPUT_HANDLE) }
        .map_err(|e| std::io::Error::other(format!("GetStdHandle 失败：{}", e.code().0)))?;
    let handle: windows::Win32::Foundation::HANDLE = handle;
    let mut n = 0u32;
    unsafe {
        ReadFile(handle, Some(buf), Some(&mut n), None)
            .map_err(|e| std::io::Error::other(format!("ReadFile 失败：{}", e.code().0)))?;
    }
    Ok(n as usize)
}

#[cfg(all(not(unix), not(windows)))]
fn read_stdin_raw(buf: &mut [u8]) -> std::io::Result<usize> {
    use std::io::Read;
    std::io::stdin().read(buf)
}

#[cfg(not(unix))]
impl LineReader {
    pub fn new(_fd: i32) -> Self {
        let (tx, rx) = std::sync::mpsc::channel();
        // 读线程在进程生命周期内阻塞在读系统调用上（事件驱动，不轮询）；
        // 会话结束由 main 的 process::exit 一并终止（无需唤醒/回收）。
        let _ = std::thread::Builder::new()
            .name("vault-stdin".into())
            .spawn(move || {
                let mut pending: Vec<u8> = Vec::new(); // 自管累计缓冲（消费后零化）
                let mut chunk = [0u8; 4096]; // 读系统调用临时缓冲（用后零化）
                loop {
                    // 先看累计缓冲里是否已有完整行（一次读可能带回多行）。
                    if let Some(idx) = pending.iter().position(|&b| b == b'\n') {
                        let end = idx + 1;
                        let emitted = {
                            let mut line = pending[..end].to_vec();
                            let d = decode_line(&line);
                            line.zeroize();
                            d
                        };
                        pending[..end].zeroize(); // 消费区间零化后再移除
                        pending.drain(..end);
                        if tx.send(LineMsg::Line(emitted)).is_err() {
                            return;
                        }
                        continue;
                    }
                    match read_stdin_raw(&mut chunk) {
                        Ok(0) => {
                            // EOF：余下无换行的尾巴按一行处理（与 unix 分支一致）。
                            if !pending.is_empty() {
                                let emitted = {
                                    let mut line = std::mem::take(&mut pending);
                                    let d = decode_line(&line);
                                    line.zeroize();
                                    d
                                };
                                let _ = tx.send(LineMsg::Line(emitted));
                            }
                            let _ = tx.send(LineMsg::Eof);
                            return;
                        }
                        Ok(n) => {
                            pending.extend_from_slice(&chunk[..n]);
                            chunk[..n].zeroize();
                        }
                        Err(_) => {
                            let _ = tx.send(LineMsg::Err);
                            return;
                        }
                    }
                }
            });
        Self { rx }
    }

    pub fn read_line(&mut self, timeout_ms: i32) -> std::result::Result<Option<String>, ReadErr> {
        use std::sync::mpsc::RecvTimeoutError;
        // 定时等待走条件变量（内核挂起，由 send 事件或到期唤醒），非轮询。
        let msg = if timeout_ms > 0 {
            match self
                .rx
                .recv_timeout(std::time::Duration::from_millis(timeout_ms as u64))
            {
                Ok(m) => m,
                Err(RecvTimeoutError::Timeout) => return Err(ReadErr::Timeout),
                // 读线程已退出（EOF/错误后又到达此处）：按 EOF 处理。
                Err(RecvTimeoutError::Disconnected) => LineMsg::Eof,
            }
        } else {
            self.rx.recv().unwrap_or(LineMsg::Eof)
        };
        match msg {
            LineMsg::Line(s) => Ok(Some(s)),
            LineMsg::Eof => Ok(None),
            LineMsg::Err => Err(ReadErr::Io),
        }
    }
}

/// 输出一行到 stdout。
///
/// 经 [`emit_bytes`] **直接写句柄**（不经过 `std::io::stdout()` 的全局行缓冲）：
/// 该缓冲曾把应答明文（`get` 的 base64 值、初始化输出的恢复码）留在堆上。
pub fn emit(s: &str) {
    emit_bytes(s.as_bytes());
    emit_bytes(b"\n");
}

/// 直接写标准输出句柄（unix fd 1 / Windows `STD_OUTPUT_HANDLE`）。
///
/// 只借用句柄、不接管所有权（`ManuallyDrop`），失败静默（协议层由应答缺失体现）。
pub fn emit_bytes(buf: &[u8]) {
    #[cfg(unix)]
    {
        let mut off = 0usize;
        while off < buf.len() {
            // SAFETY: 向 fd 1 写入 buf[off..] 的字节；返回写入量或 -1。
            let n = unsafe {
                libc::write(
                    1,
                    buf[off..].as_ptr() as *const libc::c_void,
                    buf.len() - off,
                )
            };
            if n <= 0 {
                return;
            }
            off += n as usize;
        }
    }
    #[cfg(windows)]
    {
        use std::io::Write;
        use std::mem::ManuallyDrop;
        use std::os::windows::io::FromRawHandle;
        use windows::Win32::System::Console::{GetStdHandle, STD_OUTPUT_HANDLE};
        // SAFETY: 只借用标准输出句柄（ManuallyDrop 保证不关闭它）。
        let handle = match unsafe { GetStdHandle(STD_OUTPUT_HANDLE) } {
            Ok(h) if !h.is_invalid() => h,
            _ => return,
        };
        let mut f = ManuallyDrop::new(unsafe {
            std::fs::File::from_raw_handle(handle.0 as *mut core::ffi::c_void)
        });
        let _ = f.write_all(buf);
        let _ = f.flush();
    }
    #[cfg(all(not(unix), not(windows)))]
    {
        use std::io::Write;
        let mut out = std::io::stdout().lock();
        let _ = out.write_all(buf);
        let _ = out.flush();
    }
}

enum DispatchResult {
    Continue,
    Quit,
}

pub struct ServeSession {
    service: VaultService,
    parent_pid: i32,
}

impl ServeSession {
    pub fn new(data_dir: &str) -> Result<Self> {
        let service = VaultService::new(data_dir)?;
        let parent_pid = guard::verify_and_arm()?;
        Ok(Self {
            service,
            parent_pid,
        })
    }

    pub fn run(&self) -> i32 {
        if !self.service.initialized() {
            return fail("vault 未初始化，请先 init");
        }
        let mode = match self.service.mode() {
            Ok(m) => m,
            Err(e) => return fail(&e.message()),
        };
        let slot_mode = if mode == crate::format::VaultMode::Slots {
            self.service.slots_mode().unwrap_or(SlotsMode::None)
        } else {
            SlotsMode::None
        };
        let (min_fields, max_fields) = if mode == crate::format::VaultMode::Slots {
            match slot_mode {
                SlotsMode::M2b => (3, 3),
                SlotsMode::M2a => (4, 5),
                _ => (4, 4),
            }
        } else {
            (3, 3)
        };

        let mut reader = LineReader::new(0);

        // ── 握手 ──
        let line = Zeroizing::new(match reader.read_line(HANDSHAKE_TIMEOUT_SECONDS * 1000) {
            Ok(Some(l)) => l,
            Ok(None) => String::new(),
            Err(ReadErr::Timeout) => return fail("握手超时"),
            Err(ReadErr::Io) => return fail("握手读取失败"),
        });
        let parts: Vec<&str> = line.split_whitespace().collect();
        if parts.len() < min_fields || parts.len() > max_fields || parts[0] != "handshake" {
            return fail(&handshake_usage(mode, slot_mode));
        }
        let h = match b64_decode(parts[1]) {
            Ok(v) => v,
            Err(_) => return fail("握手负载非 base64"),
        };
        let c = match b64_decode(parts[2]) {
            Ok(v) => v,
            Err(_) => return fail("握手负载非 base64"),
        };
        let secret = if parts.len() >= 4 {
            match b64_decode(parts[3]) {
                Ok(v) => Some(LockedBuffer::take(v)),
                Err(_) => return fail("握手负载非 base64"),
            }
        } else {
            None
        };
        let label = if parts.len() >= 5 {
            match b64_decode(parts[4]) {
                Ok(v) => Some(String::from_utf8_lossy(&v).into_owned()),
                Err(_) => return fail("握手负载非 base64"),
            }
        } else {
            None
        };
        if h.len() != 32 || c.len() != 16 {
            return fail("握手负载长度错误");
        }
        if slot_mode == SlotsMode::M2a {
            if let Some(l) = &label {
                if l != "rec" {
                    return fail("握手标签未知：m2a 仅支持 \"rec\"（恢复码）");
                }
            }
        }

        // 解锁失败退避（§3.7）：锁定期间连尝试都不做。
        if let Some(secs) = self.service.lockout().locked_seconds(self.service.store()) {
            return fail(&VaultError::Lockout(secs).message());
        }

        let unlocked = (|| -> Result<(LockedBuffer, Vec<u8>)> {
            let master = self
                .service
                .load_master_key_into(secret.as_ref().map(|s| s.as_slice()), label.as_deref())?;
            let anchor = self.service.get_or_create_auth_anchor(master.as_slice())?;
            Ok((master, anchor))
        })();
        let (master, anchor) = match unlocked {
            Ok(v) => v,
            Err(VaultError::NeedRecovery(m)) => return fail(&format!("NEED_RECOVERY {m}")),
            Err(e @ VaultError::ShareBackendMismatch { .. }) => {
                return fail(&format!("SHARE_BACKEND_MISMATCH {}", e.message()))
            }
            Err(VaultError::ShareMissing(m)) => return fail(&format!("SHARE_MISSING {m}")),
            Err(VaultError::AuthTag(m)) => {
                self.service.lockout().record_failure(self.service.store());
                return fail(&format!("SHARE_MISMATCH {m}"));
            }
            Err(e) => {
                self.service.lockout().record_failure(self.service.store());
                return fail(&e.message());
            }
        };
        self.service.lockout().clear(self.service.store());

        let mac = crate::crypto::hmac_sha256(&h, &c);
        emit(&format!(
            "ok handshake {} {} {}",
            b64_encode(&anchor),
            b64_encode(&mac),
            MARKER
        ));

        // ── 命令循环 ──
        loop {
            let cmd = Zeroizing::new(match reader.read_line(IDLE_TIMEOUT_SECONDS * 1000) {
                Ok(Some(l)) => l,
                Ok(None) => return 0,
                Err(ReadErr::Timeout) => return 0,
                Err(ReadErr::Io) => return 0,
            });
            match self.dispatch(&cmd, master.as_slice(), &mut reader) {
                DispatchResult::Quit => return 0,
                DispatchResult::Continue => {}
            }
            if guard::parent_gone(self.parent_pid) {
                return 0;
            }
        }
    }

    fn dispatch(&self, line: &str, master: &[u8], reader: &mut LineReader) -> DispatchResult {
        let parts: Vec<&str> = line.split_whitespace().collect();
        match parts.first() {
            Some(&"quit") => {
                emit("ok");
                DispatchResult::Quit
            }
            Some(&"status") => {
                emit("ok {\"initialized\":true}");
                DispatchResult::Continue
            }
            Some(&"get") => {
                if parts.len() != 2 {
                    return err("get 需要 uid 参数");
                }
                self.handle_get(parts[1], master)
            }
            Some(&"set") => {
                if parts.len() != 2 {
                    return err("set 需要 uid 参数");
                }
                self.handle_set(parts[1], master, reader)
            }
            Some(&"delete") => {
                if parts.len() != 2 {
                    return err("delete 需要 uid 参数");
                }
                self.handle_delete(parts[1], master)
            }
            Some(&"destroy") => {
                match self.service.destroy() {
                    Ok(()) => emit("ok"),
                    Err(e) => return err(&e.message()),
                }
                DispatchResult::Continue
            }
            Some(other) => err(&format!("未知命令: {other}")),
            None => err("未知命令: "),
        }
    }

    fn handle_get(&self, uid: &str, master: &[u8]) -> DispatchResult {
        match self.service.get_with(uid, master) {
            Ok(None) => emit("ok null"),
            Ok(Some(got)) => {
                // 解出的明文进锁定缓冲（mlock/VirtualLock）；base64 响应串用零化包装。
                // 原先两者均直接 drop，会以明文残留在堆上。
                let got = LockedBuffer::take(got);
                let resp = Zeroizing::new(format!("ok {}", b64_encode(got.as_slice())));
                emit(&resp);
            }
            Err(e) => return err(&e.message()),
        }
        DispatchResult::Continue
    }

    fn handle_set(&self, uid: &str, master: &[u8], reader: &mut LineReader) -> DispatchResult {
        if uid == AUTH_UID {
            return err("保留 uid，拒绝写入");
        }
        // 负载行与解码后的明文都短时持有（Zeroizing 用后零化）。
        let payload = Zeroizing::new(match reader.read_line(IDLE_TIMEOUT_SECONDS * 1000) {
            Ok(Some(p)) => p,
            Err(ReadErr::Timeout) => return err("set 负载读取超时"),
            _ => return err("set 缺少 stdin 负载行（base64）"),
        });
        if payload.is_empty() {
            return err("set 缺少 stdin 负载行（base64）");
        }
        let plain = LockedBuffer::take(match b64_decode(&payload) {
            Ok(p) => p,
            Err(_) => return err("set 负载非 base64"),
        });
        match self.service.set_with(uid, plain.as_slice(), master) {
            Ok(()) => emit("ok"),
            Err(e) => return err(&e.message()),
        }
        DispatchResult::Continue
    }

    fn handle_delete(&self, uid: &str, master: &[u8]) -> DispatchResult {
        match self.service.delete_with(uid, master) {
            Ok(true) => emit("ok true"),
            Ok(false) => emit("ok false"),
            Err(e) => return err(&e.message()),
        }
        DispatchResult::Continue
    }
}

fn err(message: &str) -> DispatchResult {
    emit(&format!("err {message}"));
    DispatchResult::Continue
}

fn fail(message: &str) -> i32 {
    emit(&format!("err {message}"));
    1
}

fn handshake_usage(mode: crate::format::VaultMode, slot_mode: SlotsMode) -> String {
    if mode == crate::format::VaultMode::Slots {
        return match slot_mode {
            SlotsMode::M2b => {
                "握手格式错误：m2b 需要 `handshake <b64H> <b64C>`（device.key）".to_string()
            }
            SlotsMode::M2a => "握手格式错误：m2a 需要 `handshake <b64H> <b64C> <b64password>` 或 `… <b64恢复码> <b64\"rec\">`".to_string(),
            _ => "握手格式错误：m1 需要 `handshake <b64H> <b64C> <b64password>`".to_string(),
        };
    }
    "握手格式错误：需要 `handshake <b64H> <b64C>`".to_string()
}
