// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 凭据保险库 CLI（Rust 重实现，与 C# Program.cs 字节/协议兼容）。
//!
//! 命令：ping / --version / selftest / selftest-seccomp / init-crypto /
//! init-file / init-m1 / init-m2a / init-m2b / status / set-kdf / serve / destroy。
//! 失败：stdout 输出 `err <message>` 且退出码 1。

mod crypto;
mod device;
mod encoding;
mod errors;
mod format;
mod guard;
mod lockout;
mod memguard;
mod safefile;
mod recovery;
// Linux/BSD 专用：Secret Service（libsecret）D-Bus 客户端。Windows/macOS 走 DPAPI/Keychain。
#[cfg(all(unix, not(target_os = "macos")))]
mod secret_service;
mod service;
mod share;
mod serve;

use std::io::Write;

use zeroize::Zeroizing;

use encoding::b64_encode;
use errors::{Result, VaultError};
use service::VaultService;
use share::{FileStore, NullSecretStore};

/// 构建标记（`--version` 输出、握手应答尾字段）。
pub const MARKER: &str = if cfg!(feature = "vault_testing") {
    "ARCHOERA-VAULT-TEST-v1"
} else {
    "ARCHOERA-VAULT-PROD-v1"
};

const EXIT_OK: i32 = 0;
const EXIT_ERR: i32 = 1;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    // 进程硬化（mlock 配额 / RLIMIT_CORE=0 / PR_SET_DUMPABLE=0 / seccomp / Windows 缓解）：
    // **所有命令**都要武装——`init-*`/`set-kdf`/`destroy`/`status` 同样在内存里持有
    // 口令与主密钥（`init-m2a` 还会打印恢复码），不硬化就等于「同用户可读内存 + 崩溃可写 core」。
    // 血缘校验（父进程绑定）仅 `serve` 需要，见 guard::verify_and_arm。
    if let Err(e) = guard::harden_process() {
        println!("err {}", e.message());
        std::process::exit(EXIT_ERR);
    }
    let code = match run(&args) {
        Ok(c) => c,
        Err(VaultError::SecretStoreUnavailable(m)) => {
            println!("err {m}");
            EXIT_ERR
        }
        Err(e) => {
            println!("err {}: {}", e.type_name(), e.message());
            EXIT_ERR
        }
    };
    std::process::exit(code);
}

fn run(args: &[String]) -> Result<i32> {
    if args.is_empty() {
        println!("err 缺少命令");
        return Ok(EXIT_ERR);
    }
    match args[0].as_str() {
        "ping" => {
            println!("ok pong");
            Ok(EXIT_OK)
        }
        "--version" => {
            println!("{MARKER}");
            Ok(EXIT_OK)
        }
        "selftest" => {
            if crypto::selftest() {
                println!("ok vault selftest 通过（RFC 9106 Argon2id 向量）");
                Ok(EXIT_OK)
            } else {
                println!("err vault selftest 失败");
                Ok(EXIT_ERR)
            }
        }
        "selftest-seccomp" => selftest_seccomp(args),
        "selftest-mem" => {
            // 内存护栏自检：分配一段敏感缓冲并报告是否真的锁进物理内存
            // （Linux 可另以 /proc/<pid>/status 的 VmLck 交叉验证）。
            let buf = memguard::LockedBuffer::new(64);
            println!(
                "ok memguard locked={} len={}",
                if buf.is_locked() { "true" } else { "false" },
                buf.len()
            );
            Ok(EXIT_OK)
        }
        "init-crypto" => {
            require_args(args, 2)?;
            let svc = VaultService::new(&args[1])?;
            svc.init_crypto(None)?;
            let master = svc.load_master_key_into(None, None)?;
            let t = svc.get_or_create_auth_anchor(master.as_slice())?;
            println!("ok {}", b64_encode(&t));
            Ok(EXIT_OK)
        }
        "init-file" => {
            require_args(args, 2)?;
            let use_stdin = args.get(2).map(|s| s == "--key-stdin").unwrap_or(false);
            let injected = if use_stdin {
                let line = Zeroizing::new(read_stdin_line("未读取到主密钥（stdin 第一行，hex64）")?);
                let bytes = Zeroizing::new(hex_decode(line.trim()).ok_or_else(|| {
                    VaultError::argument("主密钥非合法 hex 字符串".to_string())
                })?);
                if bytes.len() != format::KEY_SIZE {
                    return Err(VaultError::argument(format!(
                        "主密钥须为 {} 个 hex 字符",
                        format::KEY_SIZE * 2
                    )));
                }
                Some(bytes.to_vec())
            } else {
                None
            };
            let svc = VaultService::with_store(&args[1], Box::new(FileStore::new(&args[1])));
            svc.init_crypto(injected)?;
            let master = svc.load_master_key_into(None, None)?;
            let t = svc.get_or_create_auth_anchor(master.as_slice())?;
            println!("ok {}", b64_encode(&t));
            Ok(EXIT_OK)
        }
        "init-m1" => {
            require_args(args, 2)?;
            let kdf = parse_kdf_flags(args, 2)?;
            let password = Zeroizing::new(read_stdin_line("未读取到口令（stdin 第一行）")?);
            let svc = VaultService::with_store(&args[1], Box::new(NullSecretStore));
            svc.init_slots(password.as_bytes(), Some(kdf))?;
            let master = svc.load_master_key_into(Some(password.as_bytes()), None)?;
            let t = svc.get_or_create_auth_anchor(master.as_slice())?;
            println!("ok {}", b64_encode(&t));
            Ok(EXIT_OK)
        }
        "init-m2a" => {
            require_args(args, 2)?;
            let kdf = parse_kdf_flags(args, 2)?;
            let password = Zeroizing::new(read_stdin_line("未读取到口令（stdin 第一行）")?);
            let svc = VaultService::with_store(&args[1], Box::new(NullSecretStore));
            let code = Zeroizing::new(svc.init_m2a(password.as_bytes(), Some(kdf))?);
            let master = svc.load_master_key_into(Some(password.as_bytes()), None)?;
            let t = svc.get_or_create_auth_anchor(master.as_slice())?;
            // 恢复码仅此一次展示：**直接写句柄**（不经 std 全局行缓冲，避免明文
            // 滞留堆上），写后随 Zeroizing 零化。
            let line = Zeroizing::new(format!("ok {} {}", b64_encode(&t), code.as_str()));
            serve::emit_bytes(line.as_bytes());
            serve::emit_bytes(b"\n");
            Ok(EXIT_OK)
        }
        "init-m2b" => {
            require_args(args, 2)?;
            let svc = VaultService::with_store(&args[1], Box::new(NullSecretStore));
            svc.init_m2b()?;
            let master = svc.load_master_key_into(None, None)?;
            let t = svc.get_or_create_auth_anchor(master.as_slice())?;
            println!("ok {}", b64_encode(&t));
            Ok(EXIT_OK)
        }
        "status" => {
            require_args(args, 2)?;
            let svc = VaultService::new(&args[1])?;
            if !svc.initialized() {
                println!("ok {{\"initialized\":false}}");
            } else {
                let kdf = svc.current_kdf()?;
                let kdf_json = match kdf {
                    Some(k) => format!(",\"kdf\":{{\"m\":{},\"t\":{},\"p\":{}}}", k.m_kib, k.t, k.p),
                    None => String::new(),
                };
                println!(
                    "ok {{\"initialized\":true,\"mode\":\"{}\",\"backend\":\"{}\"{kdf_json}}}",
                    mode_name(&svc)?,
                    svc.backend()
                );
            }
            Ok(EXIT_OK)
        }
        "set-kdf" => {
            require_args(args, 2)?;
            let kdf = parse_kdf_flags(args, 2)?;
            let svc = VaultService::new(&args[1])?;
            let password = Zeroizing::new(read_stdin_line("未读取到口令（stdin 第一行）")?);
            match svc.change_kdf(password.as_bytes(), kdf) {
                Ok(()) => {
                    println!("ok");
                    Ok(EXIT_OK)
                }
                Err(VaultError::NeedRecovery(m)) => {
                    println!("err NEED_RECOVERY {m}");
                    Ok(EXIT_ERR)
                }
                Err(VaultError::AuthTag(m)) => {
                    println!("err SHARE_MISMATCH {m}");
                    Ok(EXIT_ERR)
                }
                Err(VaultError::InvalidOperation(m)) => {
                    println!("err {m}");
                    Ok(EXIT_ERR)
                }
                Err(e) => Err(e),
            }
        }
        "serve" => {
            require_args(args, 2)?;
            let session = serve::ServeSession::new(&args[1])?;
            Ok(session.run())
        }
        "destroy" => {
            require_args(args, 2)?;
            VaultService::new(&args[1])?.destroy()?;
            println!("ok");
            Ok(EXIT_OK)
        }
        other => {
            println!("err 未知命令: {other}");
            Ok(EXIT_ERR)
        }
    }
}

fn selftest_seccomp(args: &[String]) -> Result<i32> {
    // 武装进程护栏后报告过滤器状态，并故意触发被拒系统调用。
    guard::verify_and_arm()?;
    println!(
        "ok seccomp={}",
        if guard::seccomp_active() {
            "true"
        } else {
            "false"
        }
    );
    let _ = std::io::stdout().flush();
    if !cfg!(target_os = "linux") {
        return Ok(EXIT_OK);
    }
    // `selftest-seccomp list`：逐条验证 denylist 生效（每项 fork 一个子进程实际调用，
    // 被 SIGSYS 终止即生效）。用于「沙箱完整性」自检——过滤器装载 ≠ 名单真的拦得住。
    if args.get(1).map(|s| s == "list").unwrap_or(false) {
        let (total, killed, missed) = guard::probe_denylist();
        println!(
            "ok denylist killed={killed}/{total} failed={}",
            if missed.is_empty() {
                "-".to_string()
            } else {
                missed.join(",")
            }
        );
        return Ok(if killed == total { EXIT_OK } else { EXIT_ERR });
    }
    let domain = args
        .get(1)
        .and_then(|s| s.parse::<i32>().ok())
        .unwrap_or(2);
    let fd = unsafe { guard::probe_socket(domain) };
    println!("ok socket(domain={domain}) 放行（fd={fd}）");
    Ok(EXIT_OK)
}

fn mode_name(svc: &VaultService) -> Result<&'static str> {
    match svc.mode()? {
        format::VaultMode::Crypto => Ok("crypto"),
        format::VaultMode::Slots => Ok(match svc.slots_mode()? {
            format::SlotsMode::M2a => "m2a",
            format::SlotsMode::M2b => "m2b",
            _ => "m1",
        }),
    }
}

fn require_args(args: &[String], n: usize) -> Result<()> {
    if args.len() < n {
        return Err(VaultError::argument(format!("参数不足：需要 {}", n - 1)));
    }
    Ok(())
}

fn parse_kdf_flags(args: &[String], start: usize) -> Result<crypto::KdfParams> {
    let mut m: Option<i64> = None;
    let mut t: Option<i64> = None;
    let mut p: Option<i64> = None;
    let mut i = start;
    while i < args.len() {
        match args[i].as_str() {
            "--kdf-m" => {
                m = Some(parse_int_arg(args, i + 1, "--kdf-m")?);
                i += 2;
            }
            "--kdf-t" => {
                t = Some(parse_int_arg(args, i + 1, "--kdf-t")?);
                i += 2;
            }
            "--kdf-p" => {
                p = Some(parse_int_arg(args, i + 1, "--kdf-p")?);
                i += 2;
            }
            other => return Err(VaultError::argument(format!("未知参数：{other}"))),
        }
    }
    let d = crypto::KdfParams::DEFAULT;
    let (m, t, p) = format::validate_kdf_signed(
        m.unwrap_or(d.m_kib as i64),
        t.unwrap_or(d.t as i64),
        p.unwrap_or(d.p as i64),
    )?;
    Ok(crypto::KdfParams {
        m_kib: m,
        t,
        p,
    })
}

fn parse_int_arg(args: &[String], index: usize, flag: &str) -> Result<i64> {
    if index >= args.len() {
        return Err(VaultError::argument(format!("{flag} 缺少取值")));
    }
    args[index]
        .parse::<i64>()
        .map_err(|_| VaultError::argument(format!("{flag} 取值非整数：{}", args[index])))
}

fn read_stdin_line(eof_message: &str) -> Result<String> {
    let mut s = String::new();
    let n = std::io::stdin().read_line(&mut s)?;
    if n == 0 {
        return Err(VaultError::argument(eof_message.to_string()));
    }
    while s.ends_with('\n') || s.ends_with('\r') {
        s.pop();
    }
    Ok(s)
}

fn hex_decode(s: &str) -> Option<Vec<u8>> {
    if s.len() % 2 != 0 {
        return None;
    }
    let b = s.as_bytes();
    let mut out = Vec::with_capacity(s.len() / 2);
    let mut i = 0;
    while i < b.len() {
        let hi = hex_val(b[i])?;
        let lo = hex_val(b[i + 1])?;
        out.push((hi << 4) | lo);
        i += 2;
    }
    Some(out)
}

fn hex_val(c: u8) -> Option<u8> {
    match c {
        b'0'..=b'9' => Some(c - b'0'),
        b'a'..=b'f' => Some(c - b'a' + 10),
        b'A'..=b'F' => Some(c - b'A' + 10),
        _ => None,
    }
}
