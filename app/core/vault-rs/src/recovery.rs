// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! 恢复码（F2）：32B 恢复材料的 Crockford Base32 人类可读编码
//! （与 C# RecoveryCode.cs 逐字节/逐字符一致）。

use crate::errors::{Result, VaultError};
use crate::format::KEY_SIZE;

const ALPHABET: &[u8] = b"0123456789ABCDEFGHJKMNPQRSTVWXYZ";
pub const GROUP_SIZE: usize = 4;
pub const BYTE_SIZE: usize = KEY_SIZE;

/// 将 [data] 编码为展示串（每 4 字符一组、`-` 分隔；无填充）。
pub fn encode(data: &[u8]) -> String {
    let raw = encode_raw(data);
    let mut s = String::with_capacity(raw.len() + raw.len() / GROUP_SIZE);
    for (i, c) in raw.chars().enumerate() {
        if i > 0 && i % GROUP_SIZE == 0 {
            s.push('-');
        }
        s.push(c);
    }
    s
}

/// 归一化展示串并解码回 32 字节；长度/字符非法抛 [VaultError::InvalidData]。
pub fn decode(display: &str) -> Result<Vec<u8>> {
    let normalized = normalize(display);
    let result = decode_raw(&normalized)?;
    if result.len() != BYTE_SIZE {
        return Err(VaultError::invalid_data(format!(
            "恢复码长度异常（解码 {} 字节，应为 {}）",
            result.len(),
            BYTE_SIZE
        )));
    }
    Ok(result)
}

/// 以 UTF-8 解读握手负载后归一化并解码。
pub fn decode_utf8(utf8: &[u8]) -> Result<Vec<u8>> {
    decode(&String::from_utf8_lossy(utf8))
}

/// 归一化：转大写、去 `-`/空白、`I`/`L`→`1`、`O`→`0`。
fn normalize(input: &str) -> String {
    let mut s = String::with_capacity(input.len());
    for ch in input.chars() {
        let c = ch.to_ascii_uppercase();
        match c {
            '-' | ' ' | '\t' | '\r' | '\n' => continue,
            'I' | 'L' => s.push('1'),
            'O' => s.push('0'),
            _ => s.push(c),
        }
    }
    s
}

/// 大端 Bit 流编码（无分组填充）。
fn encode_raw(data: &[u8]) -> String {
    let mut s = String::with_capacity((data.len() * 8 + 4) / 5);
    let mut buffer: u32 = 0;
    let mut bits: u32 = 0;
    for &b in data {
        buffer = (buffer << 8) | b as u32;
        bits += 8;
        while bits >= 5 {
            bits -= 5;
            s.push(ALPHABET[((buffer >> bits) & 0x1F) as usize] as char);
        }
    }
    if bits > 0 {
        s.push(ALPHABET[((buffer << (5 - bits)) & 0x1F) as usize] as char);
    }
    s
}

/// 大端 Bit 流解码（尾部不足 8 bit 的填充丢弃）。
fn decode_raw(normalized: &str) -> Result<Vec<u8>> {
    let mut bytes = Vec::with_capacity(normalized.len() * 5 / 8);
    let mut buffer: u32 = 0;
    let mut bits: u32 = 0;
    for ch in normalized.chars() {
        let v = value_of(ch);
        if v < 0 {
            return Err(VaultError::invalid_data(format!(
                "恢复码含非法字符 '{ch}'"
            )));
        }
        buffer = (buffer << 5) | v as u32;
        bits += 5;
        if bits >= 8 {
            bits -= 8;
            bytes.push(((buffer >> bits) & 0xFF) as u8);
        }
    }
    Ok(bytes)
}

/// 归一化后字符 → 5 bit 值（无效字符返回 -1）。
fn value_of(c: char) -> i32 {
    if c.is_ascii_digit() {
        return (c as u8 - b'0') as i32;
    }
    match c {
        'A' => 10,
        'B' => 11,
        'C' => 12,
        'D' => 13,
        'E' => 14,
        'F' => 15,
        'G' => 16,
        'H' => 17,
        'J' => 18,
        'K' => 19,
        'M' => 20,
        'N' => 21,
        'P' => 22,
        'Q' => 23,
        'R' => 24,
        'S' => 25,
        'T' => 26,
        'V' => 27,
        'W' => 28,
        'X' => 29,
        'Y' => 30,
        'Z' => 31,
        _ => -1,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encode_len_and_groups() {
        let data = [0u8; 32];
        let s = encode(&data);
        assert_eq!(s.chars().filter(|c| *c != '-').count(), 52);
        assert_eq!(s.matches('-').count(), 12);
    }

    #[test]
    fn roundtrip_and_normalization() {
        let data: Vec<u8> = (0..32u8).collect();
        let s = encode(&data);
        assert_eq!(decode(&s).unwrap(), data);
        let messy = s.to_lowercase().replace('-', " ");
        assert_eq!(decode(&messy).unwrap(), data);
    }
}
