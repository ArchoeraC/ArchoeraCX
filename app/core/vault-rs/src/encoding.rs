// ArchoeraMusic
// Copyright (C) 2026 Archoera && BetaStudio2
// SPDX-License-Identifier: AGPL-3.0-or-later

//! base64（标准字母表，含填充）编码/解码。

use base64::engine::general_purpose::STANDARD;
use base64::Engine;

use crate::errors::{Result, VaultError};

pub fn b64_encode(data: &[u8]) -> String {
    STANDARD.encode(data)
}

pub fn b64_decode(s: &str) -> Result<Vec<u8>> {
    STANDARD
        .decode(s.trim())
        .map_err(|e| VaultError::argument(format!("base64 解码失败：{e}")))
}
