// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! Linux **Secret Service（`org.freedesktop.Secret`）最小 D-Bus 客户端**。
//!
//! 背景（2026-10 补实现）：`LibsecretStore` 曾是 **V1 桩**（`store()` 直接返回「未实现」），
//! 于是 Linux 上 `crypto`（LEGACY）方案无法启用——而 C# 时代的 vault 能用
//! （`SecretStores.cs` 走 libsecret/GLib：schema `archoera.vault`、`account = "{key}|{dataDir}"`）。
//! 本模块把能力补回来，**不引入任何新依赖**（纯 Rust 按线协议收发）：vault-rs 的方针是
//! 「Linux 依赖集保持不变，纯 Rust / 无 OpenSSL」，且它跑在 seccomp 沙箱内——沙箱**已放行 AF_UNIX**
//! （仅按地址族拒 AF_INET/AF_INET6，见 `guard.rs`），而 `execve` 被拒，故「起 `secret-tool`/`gdbus`
//! 子进程」不可能，只能自己走 D-Bus。
//!
//! 安全取向：**编码 / 解码都按 D-Bus 签名严格进行**（不靠字符串启发式），回复按 `reply_serial`
//! 匹配调用；任何异常（需要交互提示、服务拒绝、解析失败）一律 **fail-closed**（返回错误或视为无份额），
//! 绝不静默降级、不误读别的条目。
//!
//! 兼容与隔离：条目标签沿用 `xdg:schema = archoera.vault` + `account = "{key}|{dataDir}"`
//! （与 C# 时代逐字一致 → 老条目可读、不产生"新老各一份"）。注意 Secret Service 对**同会话内的应用
//! 没有客户端侧 ACL**——任何同会话应用都能经 D-Bus 读写该集合（除非钥匙串守护进程自身要求解锁/授权）；
//! 这是平台边界，客户端无法收紧，如实记录于 `docs/vault-scheme-comparison.md`。

use std::io::{Read, Write};
use std::os::unix::net::UnixStream;
use std::time::Duration;

use crate::errors::{Result, VaultError};

/// 老 C# 版（`SecretStores.cs`）使用的 schema 名——保持逐字一致以兼容既有条目。
pub const LEGACY_SCHEMA: &str = "archoera.vault";

/// 数据目录作用域化 account（与 C# `SecretKeyScope.Scoped` 逐字一致：`"{key}|{dataDir}"`）。
pub fn scoped_account(key: &str, data_dir: &str) -> String {
    format!("{key}|{data_dir}")
}

fn unavailable(msg: impl Into<String>) -> VaultError {
    VaultError::SecretStoreUnavailable(msg.into())
}

/* ------------------------------------------------------------------ */
/* 地址解析 / 认证                                                      */
/* ------------------------------------------------------------------ */

/// 会话总线地址（只支持本地 unix 传输：`unix:path=` / `unix:abstract=`）。
#[derive(Debug, PartialEq)]
pub enum BusAddress {
    Path(String),
    Abstract(String),
}

/// 解析 `DBUS_SESSION_BUS_ADDRESS`。
pub fn parse_bus_address(raw: &str) -> Result<BusAddress> {
    let trimmed = raw.trim();
    if trimmed.is_empty() {
        return Err(unavailable(
            "Linux Secret Service 不可用（DBUS_SESSION_BUS_ADDRESS 为空）",
        ));
    }
    let body = trimmed
        .strip_prefix("unix:")
        .ok_or_else(|| unavailable("Linux Secret Service 不可用（非 unix 传输的总线地址）"))?;
    for part in body.split(',') {
        let part = part.trim();
        if let Some(path) = part.strip_prefix("path=") {
            return Ok(BusAddress::Path(path.to_string()));
        }
        if let Some(name) = part.strip_prefix("abstract=") {
            return Ok(BusAddress::Abstract(name.to_string()));
        }
    }
    Err(unavailable(
        "Linux Secret Service 不可用（总线地址缺少 path/abstract）",
    ))
}

/// `AUTH EXTERNAL` 初始凭据：`\0` + uid 十进制文本的十六进制 ASCII + CRLF。
pub fn auth_line(uid: u32) -> Vec<u8> {
    let dec = uid.to_string();
    let mut hex = String::with_capacity(dec.len() * 2);
    for b in dec.bytes() {
        hex.push_str(&format!("{b:02x}"));
    }
    let mut line = vec![0u8];
    line.extend_from_slice(format!("AUTH EXTERNAL {hex}\r\n").as_bytes());
    line
}

/* ------------------------------------------------------------------ */
/* 线协议：类型 / 编码器                                                */
/* ------------------------------------------------------------------ */

/// 报文类型与头部字段编号（D-Bus 规范）。
const MSG_METHOD_CALL: u8 = 1;
const MSG_METHOD_RETURN: u8 = 2;
const MSG_ERROR: u8 = 3;

const FIELD_REPLY_SERIAL: u8 = 5;
const FIELD_ERROR_NAME: u8 = 4;
const FIELD_SIGNATURE: u8 = 8;
const FIELD_PATH: u8 = 1;
const FIELD_INTERFACE: u8 = 2;
const FIELD_MEMBER: u8 = 3;
const FIELD_DESTINATION: u8 = 6;

fn align_to(len: usize, to: usize) -> usize {
    let rem = len % to;
    if rem == 0 {
        0
    } else {
        to - rem
    }
}

/// 解码出的值（只保留本模块需要的形态）。
#[derive(Debug, Clone, PartialEq)]
pub enum Value {
    Byte(u8),
    Bool(bool),
    U32(u32),
    Str(String),
    Array(Vec<Value>),
    Struct(Vec<Value>),
}

impl Value {
    /// 取字符串（`s` / `o` / `g` 都解成 `Str`）。
    fn as_str(&self) -> Option<&str> {
        match self {
            Value::Str(s) => Some(s),
            _ => None,
        }
    }
    fn as_array(&self) -> Option<&[Value]> {
        match self {
            Value::Array(a) => Some(a),
            _ => None,
        }
    }
    fn as_struct(&self) -> Option<&[Value]> {
        match self {
            Value::Struct(s) => Some(s),
            _ => None,
        }
    }
}

/// 编码器（小端；自动按类型对齐）。
#[derive(Default, Debug)]
pub struct Encoder {
    buf: Vec<u8>,
}

impl Encoder {
    pub fn new() -> Self {
        Self::default()
    }
    pub fn finish(self) -> Vec<u8> {
        self.buf
    }
    fn pad(&mut self, to: usize) {
        let n = align_to(self.buf.len(), to);
        self.buf.extend(std::iter::repeat_n(0u8, n));
    }
    fn byte(&mut self, v: u8) {
        self.buf.push(v);
    }
    pub fn u32(&mut self, v: u32) {
        self.pad(4);
        self.buf.extend_from_slice(&v.to_le_bytes());
    }
    pub fn bool(&mut self, v: bool) {
        self.u32(if v { 1 } else { 0 });
    }
    pub fn str(&mut self, s: &str) {
        self.u32(s.len() as u32);
        self.buf.extend_from_slice(s.as_bytes());
        self.byte(0);
    }
    pub fn signature(&mut self, s: &str) {
        self.byte(s.len() as u8);
        self.buf.extend_from_slice(s.as_bytes());
        self.byte(0);
    }
    /// `ay`（字节数组）：长度**不含**长度字段之后的填充（与线协议实测一致）。
    pub fn byte_array(&mut self, b: &[u8]) {
        self.pad(4);
        let len_pos = self.buf.len();
        self.buf.extend_from_slice(&[0; 4]);
        let elem_start = self.buf.len();
        self.buf.extend_from_slice(b); // 元素类型 y：无需额外对齐
        let len = (self.buf.len() - elem_start) as u32;
        self.buf[len_pos..len_pos + 4].copy_from_slice(&len.to_le_bytes());
    }
    /// `a{ss}`
    pub fn dict_str_str(&mut self, pairs: &[(String, String)]) {
        self.pad(4);
        let len_pos = self.buf.len();
        self.buf.extend_from_slice(&[0; 4]);
        self.pad(8);
        let elem_start = self.buf.len();
        for (k, v) in pairs {
            self.pad(8);
            self.str(k);
            self.str(v);
        }
        let len = (self.buf.len() - elem_start) as u32;
        self.buf[len_pos..len_pos + 4].copy_from_slice(&len.to_le_bytes());
    }
    /// `ao`
    pub fn array_paths(&mut self, paths: &[String]) {
        self.pad(4);
        let len_pos = self.buf.len();
        self.buf.extend_from_slice(&[0; 4]);
        let elem_start = self.buf.len();
        for p in paths {
            self.str(p);
        }
        let len = (self.buf.len() - elem_start) as u32;
        self.buf[len_pos..len_pos + 4].copy_from_slice(&len.to_le_bytes());
    }
    /// 变体：先写内层签名，再写值（闭包负责写值）。
    ///
    /// 内层类型为**结构 / 字典项**时，其值按规范须 8 字节对齐——这里替调用方补齐
    /// （否则 `(oayays)` 这类结构会错位，解码端直接判截断）。
    pub fn variant<F: FnOnce(&mut Self)>(&mut self, inner_sig: &str, body: F) {
        self.signature(inner_sig);
        if inner_sig.starts_with('(') || inner_sig.starts_with('{') {
            self.pad(8);
        }
        body(self);
    }
}

/* ------------------------------------------------------------------ */
/* 线协议：按签名解码                                                   */
/* ------------------------------------------------------------------ */

/// 按签名解码正文（递归；支持 `y b u s o g v a ( ) { }`）。
pub fn decode_body(body: &[u8], sig: &str) -> Result<Vec<Value>> {
    let mut d = Decoder {
        buf: body,
        origin: 0,
        pos: 0,
        sig: sig.as_bytes(),
        si: 0,
    };
    let mut out = Vec::new();
    while d.si < d.sig.len() {
        out.push(d.value()?);
    }
    Ok(out)
}

struct Decoder<'a> {
    buf: &'a [u8],
    /// 本缓冲区起点在**报文**中的绝对偏移（子解码器继承，保证对齐与规范一致）。
    origin: usize,
    pos: usize,
    sig: &'a [u8],
    si: usize,
}

impl<'a> Decoder<'a> {
    fn pad(&mut self, to: usize) -> Result<()> {
        let n = align_to(self.origin + self.pos, to);
        self.pos = self
            .pos
            .checked_add(n)
            .filter(|p| *p <= self.buf.len())
            .ok_or_else(|| unavailable("D-Bus 正文截断（对齐）"))?;
        Ok(())
    }
    fn take(&mut self, n: usize) -> Result<&'a [u8]> {
        let end = self
            .pos
            .checked_add(n)
            .filter(|e| *e <= self.buf.len())
            .ok_or_else(|| unavailable("D-Bus 正文截断"))?;
        let out = &self.buf[self.pos..end];
        self.pos = end;
        Ok(out)
    }
    fn u32(&mut self) -> Result<u32> {
        self.pad(4)?;
        let b = self.take(4)?;
        Ok(u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
    }
    fn cstr(&mut self) -> Result<String> {
        let len = self.u32()? as usize;
        let b = self.take(len)?;
        let s = String::from_utf8_lossy(b).to_string();
        self.take(1)?; // NUL
        Ok(s)
    }
    fn sig_str(&mut self) -> Result<String> {
        let len = self.take(1)?[0] as usize;
        let b = self.take(len)?;
        let s = String::from_utf8_lossy(b).to_string();
        self.take(1)?; // NUL
        Ok(s)
    }
    fn value(&mut self) -> Result<Value> {
        if self.si >= self.sig.len() {
            return Err(unavailable("D-Bus 签名耗尽"));
        }
        let t = self.sig[self.si];
        self.si += 1;
        match t {
            b'y' => Ok(Value::Byte(self.take(1)?[0])),
            b'b' => Ok(Value::Bool(self.u32()? != 0)),
            b'u' => Ok(Value::U32(self.u32()?)),
            b's' | b'o' => Ok(Value::Str(self.cstr()?)),
            b'g' => Ok(Value::Str(self.sig_str()?)),
            b'v' => {
                let inner = self.sig_str()?;
                // 内层值用**子解码器**解析（签名是局部串；origin 继承以保证对齐正确），
                // 再按其消耗字节数推进本解码器。
                let mut sub = Decoder {
                    buf: &self.buf[self.pos..],
                    origin: self.origin + self.pos,
                    pos: 0,
                    sig: inner.as_bytes(),
                    si: 0,
                };
                let v = sub.value()?;
                self.pos += sub.pos;
                Ok(v)
            }
            b'a' => {
                // 元素签名只解析一次（`{...}` / `(...)` 可复用），每个元素按它独立解码。
                let elem_len = type_spec_len(self.sig, self.si)?;
                let elem_sig = self.sig[self.si..self.si + elem_len].to_vec();
                self.si += elem_len;
                let elem_type = self.sig[self.si - elem_len];

                let len = self.u32()? as usize;
                self.pad(align_of_type(elem_type))?;
                let end = self
                    .pos
                    .checked_add(len)
                    .filter(|e| *e <= self.buf.len())
                    .ok_or_else(|| unavailable("D-Bus 数组越界"))?;
                let mut items = Vec::new();
                while self.pos < end {
                    let mut sub = Decoder {
                        buf: &self.buf[self.pos..],
                        origin: self.origin + self.pos,
                        pos: 0,
                        sig: &elem_sig,
                        si: 0,
                    };
                    let v = sub.value()?;
                    if sub.pos == 0 {
                        return Err(unavailable("D-Bus 数组元素未消耗字节"));
                    }
                    self.pos += sub.pos;
                    items.push(v);
                }
                if self.pos != end {
                    return Err(unavailable("D-Bus 数组长度与元素不符"));
                }
                Ok(Value::Array(items))
            }
            b'(' => {
                self.pad(8)?;
                let mut items = Vec::new();
                let mut depth = 1;
                while self.si < self.sig.len() {
                    match self.sig[self.si] {
                        b')' if depth == 1 => {
                            self.si += 1;
                            break;
                        }
                        b'(' | b'{' => depth += 1,
                        b')' | b'}' => depth -= 1,
                        _ => {}
                    }
                    items.push(self.value()?);
                }
                Ok(Value::Struct(items))
            }
            b'{' => {
                self.pad(8)?;
                let k = self.value()?;
                let v = self.value()?;
                // 吃掉 '}'
                if self.sig.get(self.si) == Some(&b'}') {
                    self.si += 1;
                }
                Ok(Value::Struct(vec![k, v]))
            }
            other => Err(unavailable(format!("D-Bus 未支持的类型: {}", other as char))),
        }
    }
}

/// 从 `sig[start]` 起一个**完整类型**的字节长度（数组的元素签名需复用同一份）。
fn type_spec_len(sig: &[u8], start: usize) -> Result<usize> {
    let mut i = start;
    let mut depth = 0usize;
    while i < sig.len() {
        match sig[i] {
            b'y' | b'b' | b'u' | b's' | b'o' | b'g' | b'v' => {
                if depth == 0 {
                    return Ok(i + 1 - start);
                }
                i += 1;
            }
            b'a' => {
                if depth == 0 {
                    // 数组：自身 1 字节 + 元素类型
                    return Ok(1 + type_spec_len(sig, start + 1)?);
                }
                i += 1;
            }
            b'(' | b'{' => {
                depth += 1;
                i += 1;
            }
            b')' | b'}' => {
                depth = depth.saturating_sub(1);
                i += 1;
                if depth == 0 {
                    return Ok(i - start);
                }
            }
            _ => return Err(unavailable("D-Bus 签名含未知类型")),
        }
    }
    Err(unavailable("D-Bus 签名不完整"))
}

fn align_of_type(t: u8) -> usize {
    match t {
        b'y' | b'g' | b'v' => 1,
        b'(' | b'{' => 8,
        _ => 4,
    }
}

/* ------------------------------------------------------------------ */
/* 报文层                                                               */
/* ------------------------------------------------------------------ */

/// 调用结果：正常回复值，或对端返回的 D-Bus 错误名（区别于传输故障）。
pub enum CallOutcome {
    Reply(Vec<Value>),
    Error(String),
}

#[derive(Debug)]
struct Message {
    kind: u8,
    reply_serial: u32,
    error_name: String,
    body_sig: String,
    body: Vec<u8>,
}

/// 会话总线连接（含认证与 `Hello`）。
pub struct SessionBus {
    stream: UnixStream,
    serial: u32,
}

impl SessionBus {
    pub fn connect() -> Result<Self> {
        let raw = std::env::var("DBUS_SESSION_BUS_ADDRESS").unwrap_or_default();
        let stream = match parse_bus_address(&raw)? {
            BusAddress::Path(p) => UnixStream::connect(p),
            BusAddress::Abstract(name) => {
                use std::os::linux::net::SocketAddrExt;
                let addr = std::os::unix::net::SocketAddr::from_abstract_name(name.as_bytes())
                    .map_err(|e| unavailable(format!("D-Bus 抽象地址构造失败: {e}")))?;
                UnixStream::connect_addr(&addr)
            }
        }
        .map_err(|e| unavailable(format!("D-Bus 连接失败（桌面会话钥匙串不可用？）: {e}")))?;
        stream.set_read_timeout(Some(Duration::from_secs(10))).ok();
        stream.set_write_timeout(Some(Duration::from_secs(10))).ok();
        let mut bus = SessionBus { stream, serial: 0 };
        bus.authenticate()?;
        bus.call(
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus",
            "org.freedesktop.DBus",
            "Hello",
            "",
            Encoder::new(),
        )?;
        Ok(bus)
    }

    fn authenticate(&mut self) -> Result<()> {
        let uid = unsafe { libc::getuid() };
        self.stream
            .write_all(&auth_line(uid))
            .map_err(|e| unavailable(format!("D-Bus 认证写入失败: {e}")))?;
        let reply = self.read_auth_line()?;
        if !reply.starts_with("OK ") {
            return Err(unavailable(format!("D-Bus 认证被拒: {reply}")));
        }
        self.stream
            .write_all(b"BEGIN\r\n")
            .map_err(|e| unavailable(format!("D-Bus BEGIN 失败: {e}")))?;
        Ok(())
    }

    fn read_auth_line(&mut self) -> Result<String> {
        let mut out = Vec::new();
        loop {
            let mut b = [0u8; 1];
            self.stream
                .read_exact(&mut b)
                .map_err(|e| unavailable(format!("D-Bus 认证读取失败: {e}")))?;
            match b[0] {
                b'\n' => break,
                b'\r' => {}
                c => out.push(c),
            }
            if out.len() > 4096 {
                return Err(unavailable("D-Bus 认证响应过长"));
            }
        }
        Ok(String::from_utf8_lossy(&out).to_string())
    }

    /// 发送方法调用并等待**匹配 serial** 的回复；对端返回 D-Bus 错误时转成 `Err`。
    pub fn call(
        &mut self,
        destination: &str,
        path: &str,
        interface: &str,
        member: &str,
        body_sig: &str,
        body: Encoder,
    ) -> Result<Vec<Value>> {
        match self.call_outcome(destination, path, interface, member, body_sig, body)? {
            CallOutcome::Reply(vals) => Ok(vals),
            CallOutcome::Error(name) => Err(unavailable(format!(
                "Secret Service 调用 {member} 失败: {name}"
            ))),
        }
    }

    /// 与 [Self::call] 相同，但**区分**「对端返回 D-Bus 错误」（`CallOutcome::Error`）与传输故障
    /// ——兼容性探测（如 KDE 的 CreateItem 签名差异）需要按错误名分支。
    pub fn call_outcome(
        &mut self,
        destination: &str,
        path: &str,
        interface: &str,
        member: &str,
        body_sig: &str,
        body: Encoder,
    ) -> Result<CallOutcome> {
        self.serial += 1;
        let serial = self.serial;
        let msg = encode_call(
            serial,
            destination,
            path,
            interface,
            member,
            body_sig,
            &body.finish(),
        );
        self.stream
            .write_all(&msg)
            .map_err(|e| unavailable(format!("D-Bus 写入失败: {e}")))?;
        loop {
            let m = self.read_message()?;
            if m.reply_serial != serial {
                continue; // 信号 / 其它调用的回复
            }
            if m.kind == MSG_ERROR {
                return Ok(CallOutcome::Error(m.error_name));
            }
            if m.kind != MSG_METHOD_RETURN {
                continue;
            }
            return Ok(CallOutcome::Reply(decode_body(&m.body, &m.body_sig)?));
        }
    }

    fn read_message(&mut self) -> Result<Message> {
        let mut fixed = [0u8; 16];
        self.stream
            .read_exact(&mut fixed)
            .map_err(|e| unavailable(format!("D-Bus 读取失败: {e}")))?;
        if fixed[0] != b'l' {
            return Err(unavailable("D-Bus 报文非小端（不支持）"));
        }
        let kind = fixed[1];
        let body_len = u32::from_le_bytes([fixed[4], fixed[5], fixed[6], fixed[7]]) as usize;
        let fields_len = u32::from_le_bytes([fixed[12], fixed[13], fixed[14], fixed[15]]) as usize;
        let mut fields = vec![0u8; fields_len];
        self.stream
            .read_exact(&mut fields)
            .map_err(|e| unavailable(format!("D-Bus 头部读取失败: {e}")))?;
        // 头部字段与正文之间按规范有 8 字节对齐填充（FIELDS_LEN 不含填充）
        let gap = align_to(16 + fields_len, 8);
        if gap > 0 {
            let mut skip = vec![0u8; gap];
            self.stream
                .read_exact(&mut skip)
                .map_err(|e| unavailable(format!("D-Bus 对齐填充读取失败: {e}")))?;
        }
        let body = if body_len > 0 {
            let mut b = vec![0u8; body_len];
            self.stream
                .read_exact(&mut b)
                .map_err(|e| unavailable(format!("D-Bus 正文读取失败: {e}")))?;
            b
        } else {
            Vec::new()
        };
        let (reply_serial, error_name, body_sig) = parse_header(&fields);
        Ok(Message {
            kind,
            reply_serial,
            error_name,
            body_sig,
            body,
        })
    }
}

/// 解析头部字段数组——**注意**：其长度在固定头（offset 12），传进来的 `fields` 是数组**内容**，
/// 因此按元素签名 `(yv)` 逐个解析，而不是当 `a(yv)`（那样会去找不存在的长度前缀）。
fn parse_header(fields: &[u8]) -> (u32, String, String) {
    let mut items: Vec<Value> = Vec::new();
    let mut pos = 0usize;
    while pos < fields.len() {
        let mut d = Decoder {
            buf: &fields[pos..],
            origin: pos, // 头部内容起点在报文中 8 字节对齐，用绝对偏移保证对齐正确
            pos: 0,
            sig: b"(yv)",
            si: 0,
        };
        match d.value() {
            Ok(v) => {
                pos += d.pos;
                items.push(v);
            }
            Err(_) => return (0, String::new(), String::new()),
        }
    }
    let (mut reply_serial, mut err, mut body_sig) = (0u32, String::new(), String::new());
    {
        {
            for it in &items {
                if let Value::Struct(kv) = it {
                if kv.len() != 2 {
                    continue;
                }
                let code = match kv[0] {
                    Value::Byte(b) => b,
                    _ => continue,
                };
                    match code {
                        FIELD_REPLY_SERIAL => {
                            if let Value::U32(v) = kv[1] {
                                reply_serial = v;
                            }
                        }
                        FIELD_ERROR_NAME => {
                            if let Some(s) = kv[1].as_str() {
                                err = s.to_string();
                            }
                        }
                        FIELD_SIGNATURE => {
                            if let Some(s) = kv[1].as_str() {
                                body_sig = s.to_string();
                            }
                        }
                        _ => {}
                    }
                }
            }
        }
    }
    (reply_serial, err, body_sig)
}

fn encode_call(
    serial: u32,
    destination: &str,
    path: &str,
    interface: &str,
    member: &str,
    body_sig: &str,
    body: &[u8],
) -> Vec<u8> {
    let mut fields: Vec<u8> = Vec::new();
    let add = |fields: &mut Vec<u8>, code: u8, vt: &str, value: Vec<u8>| {
        while align_to(fields.len(), 8) != 0 {
            fields.push(0);
        }
        fields.push(code);
        fields.push(vt.len() as u8);
        fields.extend_from_slice(vt.as_bytes());
        fields.push(0);
        fields.extend_from_slice(&value);
    };
    let s = |v: &str| {
        let mut e = Encoder::new();
        e.str(v);
        e.finish()
    };
    add(&mut fields, FIELD_PATH, "o", s(path));
    add(&mut fields, FIELD_INTERFACE, "s", s(interface));
    add(&mut fields, FIELD_MEMBER, "s", s(member));
    add(&mut fields, FIELD_DESTINATION, "s", s(destination));
    if !body_sig.is_empty() {
        let mut e = Encoder::new();
        e.signature(body_sig);
        add(&mut fields, FIELD_SIGNATURE, "g", e.finish());
    }

    let mut e = Encoder::new();
    e.byte(b'l');
    e.byte(MSG_METHOD_CALL);
    e.byte(0);
    e.byte(1);
    e.u32(body.len() as u32);
    e.u32(serial);
    // 头部字段数组 a(yv)：长度**不含**长度字段之后的填充（实测语义，见单测）。
    e.pad(4);
    let len_pos = e.buf.len();
    e.buf.extend_from_slice(&[0; 4]);
    e.pad(8);
    let elem_start = e.buf.len();
    e.buf.extend_from_slice(&fields);
    let len = (e.buf.len() - elem_start) as u32;
    e.buf[len_pos..len_pos + 4].copy_from_slice(&len.to_le_bytes());
    // 正文起点必须与**报文** 8 字节对齐（守护进程按此定位正文；不对齐会一直等字节）。
    e.pad(8);
    e.buf.extend_from_slice(body);
    e.finish()
}

/* ------------------------------------------------------------------ */
/* Secret Service 高层操作                                              */
/* ------------------------------------------------------------------ */

const SERVICE: &str = "org.freedesktop.secrets";
const SERVICE_PATH: &str = "/org/freedesktop/secrets";
const IFACE_SERVICE: &str = "org.freedesktop.Secret.Service";
const IFACE_COLLECTION: &str = "org.freedesktop.Secret.Collection";
const IFACE_ITEM: &str = "org.freedesktop.Secret.Item";
const CONTENT_TYPE: &str = "application/octet-stream";

/// 是否需要交互提示：Secret Service 约定未解锁/需要授权时返回 prompt 对象路径，
/// **`/`（或空）表示无需提示**（libsecret 亦按此判断）。
fn needs_prompt(prompt: &str) -> bool {
    !prompt.is_empty() && prompt != "/"
}

fn attributes(schema: &str, key: &str, data_dir: &str) -> Vec<(String, String)> {
    vec![
        ("xdg:schema".to_string(), schema.to_string()),
        ("account".to_string(), scoped_account(key, data_dir)),
    ]
}

/// 查条目并取密文；不存在 → `None`；需要交互解锁 / 服务异常 → 明确报错（fail-closed）。
pub fn lookup_secret(
    bus: &mut SessionBus,
    schema: &str,
    key: &str,
    data_dir: &str,
) -> Result<Option<Vec<u8>>> {
    let mut e = Encoder::new();
    e.dict_str_str(&attributes(schema, key, data_dir));
    let items = object_paths(bus.call(SERVICE, SERVICE_PATH, IFACE_SERVICE, "SearchItems", "a{ss}", e)?)?;
    if items.is_empty() {
        return Ok(None);
    }
    let session = open_plain_session(bus)?;
    let unlocked = unlock(bus, &items)?;
    if unlocked.is_empty() {
        return Err(unavailable(
            "系统钥匙串处于锁定状态（需要交互解锁）：请先解锁桌面钥匙串后重试",
        ));
    }
    let mut e = Encoder::new();
    e.str(&session);
    let vals = bus.call(SERVICE, &unlocked[0], IFACE_ITEM, "GetSecret", "o", e)?;
    secret_of(&vals).map(Some).ok_or_else(|| {
        unavailable("Secret Service 返回的条目结构不符合 (oayays)")
    })
}

/// `a{sv}` properties：Label / Attributes /（可选）Secret。
fn properties(label: &str, attrs: &[(String, String)], secret: Option<(&str, &[u8])>) -> Encoder {
    let mut e = Encoder::new();
    e.pad(4);
    let start = e.buf.len();
    e.buf.extend_from_slice(&[0; 4]);
    e.pad(8);
    let elem_start = e.buf.len();
    e.pad(8);
    e.str("org.freedesktop.Secret.Item.Label");
    e.variant("s", |e| e.str(label));
    e.pad(8);
    e.str("org.freedesktop.Secret.Item.Attributes");
    e.variant("a{ss}", |e| e.dict_str_str(attrs));
    if let Some((session, value)) = secret {
        e.pad(8);
        e.str("org.freedesktop.Secret.Item.Secret");
        e.variant("(oayays)", |e| {
            e.str(session);
            e.byte_array(&[]); // 参数（未用）
            e.byte_array(value); // 密文
            e.str(CONTENT_TYPE);
        });
    }
    let len = (e.buf.len() - elem_start) as u32;
    e.buf[start..start + 4].copy_from_slice(&len.to_le_bytes());
    e
}

/// 写入 / 覆盖条目（默认集合）。
///
/// 先用**规范**签名 `CreateItem(a{sv} properties, a{ss} attributes, o replace, b replace)`；
/// 若对端报 `UnknownMethod`/`InvalidArgs`（KDE 的 ksecretd/kwalletd 用的是偏离规范的
/// `CreateItem(a{sv} properties, (oayays) secret, b replace)`），再按 KDE 形态重试
/// —— libsecret 同样有这层兼容，C# 时代的 vault 走 libsecret 才能在 KDE 上可用。
pub fn store_secret(
    bus: &mut SessionBus,
    schema: &str,
    key: &str,
    data_dir: &str,
    value: &[u8],
) -> Result<()> {
    let session = open_plain_session(bus)?;
    let collection = default_collection(bus)?;
    let attrs = attributes(schema, key, data_dir);
    let label = format!("ArchoeraMusic {key}");

    let mut standard = properties(&label, &attrs, Some((&session, value)));
    standard.dict_str_str(&attrs);
    standard.str("/"); // replace：object path 不能为空（libsecret 亦用 "/"）
    standard.bool(true);

    let outcome = bus.call_outcome(
        SERVICE,
        &collection,
        IFACE_COLLECTION,
        "CreateItem",
        "a{sv}a{ss}ob",
        standard,
    )?;
    let vals = match outcome {
        CallOutcome::Reply(vals) => vals,
        CallOutcome::Error(name) => {
            if !name.ends_with("UnknownMethod") && !name.ends_with("InvalidArgs") {
                return Err(unavailable(format!("Secret Service CreateItem 失败: {name}")));
            }
            // KDE 形态：secret 作为独立参数（类型即 `(oayays)`，不是变体）、无 attributes/replace path
            let mut kde = properties(&label, &attrs, None);
            kde.pad(8);
            kde.str(&session);
            kde.byte_array(&[]);
            kde.byte_array(value);
            kde.str(CONTENT_TYPE);
            kde.bool(true);
            bus.call(
                SERVICE,
                &collection,
                IFACE_COLLECTION,
                "CreateItem",
                "a{sv}(oayays)b",
                kde,
            )?
        }
    };
    // 回复 `(o item, o prompt)` → 正文签名 "oo"（两个顶层值），标准与 KDE 形态一致
    let item = vals
        .first()
        .and_then(|v| v.as_str())
        .unwrap_or_default()
        .to_string();
    let prompt = vals
        .get(1)
        .and_then(|v| v.as_str())
        .unwrap_or_default()
        .to_string();
    if needs_prompt(&prompt) {
        return Err(unavailable(format!(
            "系统钥匙串需要交互确认才能写入（prompt={prompt}）：请在桌面会话解锁后重试"
        )));
    }
    if item.is_empty() {
        return Err(unavailable("Secret Service 未返回条目路径"));
    }
    Ok(())
}

/// 删除条目（幂等）。
pub fn delete_secret(
    bus: &mut SessionBus,
    schema: &str,
    key: &str,
    data_dir: &str,
) -> Result<()> {
    let mut e = Encoder::new();
    e.dict_str_str(&attributes(schema, key, data_dir));
    let items = object_paths(bus.call(SERVICE, SERVICE_PATH, IFACE_SERVICE, "SearchItems", "a{ss}", e)?)?;
    for path in items {
        bus.call(SERVICE, &path, IFACE_ITEM, "Delete", "", Encoder::new())?;
    }
    Ok(())
}

fn open_plain_session(bus: &mut SessionBus) -> Result<String> {
    let mut e = Encoder::new();
    e.str("plain");
    e.variant("s", |e| e.str(""));
    let vals = bus.call(SERVICE, SERVICE_PATH, IFACE_SERVICE, "OpenSession", "sv", e)?;
    vals.iter()
        .find_map(|v| v.as_str().filter(|s| s.starts_with('/')).map(str::to_string))
        .ok_or_else(|| unavailable("Secret Service 未返回会话路径"))
}

/// `Unlock(ao)` → 已解锁路径（prompt 非空 = 需要交互 → 返回空集合）。
fn unlock(bus: &mut SessionBus, items: &[String]) -> Result<Vec<String>> {
    let mut e = Encoder::new();
    e.array_paths(items);
    let vals = bus.call(SERVICE, SERVICE_PATH, IFACE_SERVICE, "Unlock", "ao", e)?;
    // (ao unlocked, o prompt)
    let unlocked = vals
        .first()
        .and_then(|v| v.as_array())
        .map(|a| a.iter().filter_map(|x| x.as_str().map(str::to_string)).collect())
        .unwrap_or_else(Vec::new);
    let prompt = vals
        .get(1)
        .and_then(|v| v.as_str())
        .unwrap_or_default()
        .to_string();
    if needs_prompt(&prompt) {
        return Ok(Vec::new());
    }
    Ok(unlocked)
}

fn default_collection(bus: &mut SessionBus) -> Result<String> {
    let mut e = Encoder::new();
    e.str("default");
    let vals = bus.call(
        SERVICE,
        SERVICE_PATH,
        IFACE_SERVICE,
        "ReadAlias",
        "s",
        e,
    )?;
    vals.iter()
        .find_map(|v| v.as_str().filter(|s| s.starts_with('/')).map(str::to_string))
        .ok_or_else(|| unavailable("找不到默认密钥集合"))
}

/// `ao` 返回值 → 路径列表。
fn object_paths(vals: Vec<Value>) -> Result<Vec<String>> {
    let arr = vals
        .first()
        .and_then(|v| v.as_array())
        .ok_or_else(|| unavailable("Secret Service 返回值不是对象路径数组"))?;
    Ok(arr
        .iter()
        .filter_map(|v| v.as_str().map(str::to_string))
        .collect())
}

/// `(oayays)` 返回值 → 密文。
fn secret_of(vals: &[Value]) -> Option<Vec<u8>> {
    let st = vals.first()?.as_struct()?;
    match st.get(2)? {
        Value::Array(items) => Some(
            items
                .iter()
                .filter_map(|v| match v {
                    Value::Byte(b) => Some(*b),
                    _ => None,
                })
                .collect(),
        ),
        _ => None,
    }
}

/* ------------------------------------------------------------------ */

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_address_forms() {
        assert_eq!(
            parse_bus_address("unix:path=/run/user/1000/bus").unwrap(),
            BusAddress::Path("/run/user/1000/bus".into())
        );
        assert_eq!(
            parse_bus_address("unix:abstract=/tmp/dbus-X,guid=abc").unwrap(),
            BusAddress::Abstract("/tmp/dbus-X".into())
        );
        assert!(parse_bus_address("").is_err());
        assert!(parse_bus_address("tcp:host=127.0.0.1").is_err());
        assert!(parse_bus_address("unix:guid=abc").is_err());
    }

    #[test]
    fn auth_line_external_uid_hex() {
        assert_eq!(
            String::from_utf8_lossy(&auth_line(1000)[1..]).to_string(),
            "AUTH EXTERNAL 31303030\r\n"
        );
        assert_eq!(
            String::from_utf8_lossy(&auth_line(0)[1..]).to_string(),
            "AUTH EXTERNAL 30\r\n"
        );
    }

    #[test]
    fn scoped_account_matches_legacy_csharp_format() {
        assert_eq!(
            scoped_account("master-share", "/home/u/.local/share/ArchoeraMusic"),
            "master-share|/home/u/.local/share/ArchoeraMusic"
        );
    }

    #[test]
    fn roundtrip_a_ss_and_ao() {
        let pairs = vec![
            ("xdg:schema".to_string(), "archoera.vault".to_string()),
            ("account".to_string(), "master-share|/d".to_string()),
        ];
        let mut e = Encoder::new();
        e.dict_str_str(&pairs);
        let vals = decode_body(&e.buf, "a{ss}").unwrap();
        match &vals[0] {
            Value::Array(items) => {
                assert_eq!(items.len(), 2);
                let first = items[0].as_struct().unwrap();
                assert_eq!(first[0].as_str(), Some("xdg:schema"));
                assert_eq!(first[1].as_str(), Some("archoera.vault"));
            }
            other => panic!("期望数组，得到 {other:?}"),
        }

        let paths = vec!["/org/freedesktop/secrets/collection/login".to_string()];
        let mut e = Encoder::new();
        e.array_paths(&paths);
        let vals = decode_body(&e.buf, "ao").unwrap();
        assert_eq!(vals[0].as_array().unwrap().len(), 1);
        assert_eq!(
            vals[0].as_array().unwrap()[0].as_str(),
            Some("/org/freedesktop/secrets/collection/login")
        );
    }

    #[test]
    fn roundtrip_secret_struct_with_variant() {
        // a{sv} 里塞 (oayays)：模拟 CreateItem 的 properties
        let mut e = Encoder::new();
        e.pad(4);
        let start = e.buf.len();
        e.buf.extend_from_slice(&[0; 4]);
        e.pad(8);
        let elem = e.buf.len();
        e.pad(8);
        e.str("org.freedesktop.Secret.Item.Secret");
        e.signature("(oayays)"); // 手写 variant（不经过 Encoder::variant 的对齐补齐）
        e.pad(8);
        e.str("/session");
        e.byte_array(&[]);
        e.byte_array(&[1, 2, 3, 4]);
        e.str("application/octet-stream");
        let len = (e.buf.len() - elem) as u32;
        e.buf[start..start + 4].copy_from_slice(&len.to_le_bytes());

        let vals = decode_body(&e.buf, "a{sv}").unwrap();
        let arr = vals[0].as_array().unwrap();
        let entry = arr[0].as_struct().unwrap();
        assert_eq!(entry[0].as_str(), Some("org.freedesktop.Secret.Item.Secret"));
        // 变体折叠为内值 → 期望 (oayays) 结构
        let st = entry[1].as_struct().unwrap();
        assert_eq!(st[0].as_str(), Some("/session"));
        let secret = secret_of(&[entry[1].clone()]).unwrap();
        assert_eq!(secret, vec![1, 2, 3, 4]);
    }

    #[test]
    fn header_parse_extracts_reply_serial_and_signature() {
        let mut fields: Vec<u8> = Vec::new();
        let add = |fields: &mut Vec<u8>, code: u8, vt: &str, value: Vec<u8>| {
            while align_to(fields.len(), 8) != 0 {
                fields.push(0);
            }
            fields.push(code);
            fields.push(vt.len() as u8);
            fields.extend_from_slice(vt.as_bytes());
            fields.push(0);
            fields.extend_from_slice(&value);
        };
        let mut u = Encoder::new();
        u.u32(7);
        add(&mut fields, FIELD_REPLY_SERIAL, "u", u.finish());
        let mut g = Encoder::new();
        g.signature("(oayays)");
        add(&mut fields, FIELD_SIGNATURE, "g", g.finish());
        let mut e = Encoder::new();
        e.byte(b'y');
        add(&mut fields, 1, "y", e.finish()); // PATH（解析时被忽略）

        let (reply_serial, err, sig) = parse_header(&fields);
        assert_eq!(reply_serial, 7);
        assert!(err.is_empty());
        assert_eq!(sig, "(oayays)");
    }

    #[test]
    fn call_encodes_signature_field_and_body_length() {
        let mut body = Encoder::new();
        body.str("default");
        let body_bytes = body.finish();
        let msg = encode_call(
            3,
            SERVICE,
            SERVICE_PATH,
            IFACE_SERVICE,
            "ReadAlias",
            "s",
            &body_bytes,
        );
        assert_eq!(msg[0], b'l');
        assert_eq!(msg[1], MSG_METHOD_CALL);
        assert_eq!(
            u32::from_le_bytes([msg[4], msg[5], msg[6], msg[7]]) as usize,
            body_bytes.len()
        );
        let head = String::from_utf8_lossy(&msg[..msg.len() - body_bytes.len()]).to_string();
        assert!(head.contains("ReadAlias"));
        assert!(head.contains("org.freedesktop.Secret.Service"));
        assert!(head.contains("/org/freedesktop/secrets"));
    }

    /// 真机端到端（**env 门控**）：需要对活的 Secret Service。
    /// 本地/CI 用 `ARCHOERA_SECRET_SERVICE_E2E=1 cargo test --bin archoera-vault -- --nocapture` 启用；
    /// 无桌面钥匙串的环境自动跳过（不因环境差异红）。
    #[test]
    fn e2e_store_lookup_delete_against_live_secret_service() {
        if std::env::var("ARCHOERA_SECRET_SERVICE_E2E").as_deref() != Ok("1") {
            eprintln!("跳过：未设置 ARCHOERA_SECRET_SERVICE_E2E=1");
            return;
        }
        let mut bus = SessionBus::connect().expect("连接 session bus");
        // 固定目录（非 pid 派生）：即便上一轮在写后崩溃，预清理也能命中残留条目，
        // 不会在用户钥匙串里堆积测试条目。
        let dir = std::env::temp_dir().join("archoera-vault-e2e");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let dir = dir.to_string_lossy().to_string();
        let key = "e2e-master-share";

        // 幂等清理（上次残留）
        delete_secret(&mut bus, LEGACY_SCHEMA, key, &dir).expect("预清理");

        let payload = b"e2e-secret-payload-0123456789".to_vec();
        store_secret(&mut bus, LEGACY_SCHEMA, key, &dir, &payload).expect("写入钥匙串");
        let got = lookup_secret(&mut bus, LEGACY_SCHEMA, key, &dir).expect("读取钥匙串");
        assert_eq!(got.as_deref(), Some(payload.as_slice()), "读回应与写入一致");

        // 作用域隔离：换一个数据目录（同 key）不应命中
        let other = format!("{dir}-other");
        std::fs::create_dir_all(&other).unwrap();
        let miss = lookup_secret(&mut bus, LEGACY_SCHEMA, key, &other).expect("跨目录查询");
        assert!(miss.is_none(), "不同数据目录不应串读份额");
        let _ = std::fs::remove_dir_all(&other);

        delete_secret(&mut bus, LEGACY_SCHEMA, key, &dir).expect("删除");
        let after = lookup_secret(&mut bus, LEGACY_SCHEMA, key, &dir).expect("删除后再查");
        assert!(after.is_none(), "删除后应查不到");
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// 回归：数组长度**不含**长度字段之后的填充（实测 gdbus `SearchItems` 的 `a{ss}`：L=35，其后 39 字节含 4 字节填充）。
    #[test]
    fn array_length_excludes_padding() {
        let mut e = Encoder::new();
        e.dict_str_str(&[("xdg:schema".to_string(), "archoera.vault".to_string())]);
        let buf = e.finish();
        let l = u32::from_le_bytes([buf[0], buf[1], buf[2], buf[3]]) as usize;
        // 长度字段后：4 字节填充到 8 + 内容（键 15 + 1 填充 + 值 19 = 35）
        assert_eq!(l, 35, "数组长度应为内容字节数（不含填充）");
        assert_eq!(buf.len(), 4 + 4 + 35, "长度字段 + 填充 + 内容");
    }

    /// 回归：正文起点与报文 8 字节对齐（gdbus 的空正文 Hello 报文为 128 字节即由此而来）。
    #[test]
    fn method_call_pads_body_start_to_8() {
        let msg = encode_call(
            1,
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus",
            "org.freedesktop.DBus",
            "Hello",
            "",
            &[],
        );
        let fields_len = u32::from_le_bytes([msg[12], msg[13], msg[14], msg[15]]) as usize;
        let body_start = 16 + fields_len + align_to(16 + fields_len, 8);
        assert_eq!(body_start % 8, 0, "正文起点必须 8 字节对齐");
        assert_eq!(msg.len(), body_start, "空正文时报文长度即正文起点");
    }

    #[test]
    fn needs_prompt_only_for_real_prompt_paths() {
        // Secret Service 约定：`/` 或空 = 无需提示（libsecret 同判）
        assert!(!needs_prompt(""));
        assert!(!needs_prompt("/"));
        assert!(needs_prompt("/org/freedesktop/secrets/prompt/u1"));
    }

    #[test]
    fn decoder_rejects_truncated_body() {
        let mut e = Encoder::new();
        e.str("/path");
        let mut buf = e.finish();
        buf.truncate(buf.len() - 3);
        assert!(decode_body(&buf, "o").is_err());
    }
}
