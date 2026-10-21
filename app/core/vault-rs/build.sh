#!/usr/bin/env bash
# ArchoeraCX vault（凭据保险库）Rust 构建脚本（幂等）。
#
# 产物：vault-rs/build/archoera-vault —— **静态链接**单文件可执行（无动态库依赖），
# 按需会话进程，经 stdin/stdout 行协议服务凭据（init/set/get/delete/destroy/status）。
#
# 依赖：Rust 工具链（cargo/rustc）。纯 Rust 加密栈，无 OpenSSL。
# 静态链接（两种模式产物均为 `file` 报 "statically linked" 的单文件）：
#   - musl（已装 x86_64-unknown-linux-musl，CI 由 build-linux action 预装；或 --musl）：
#       RUSTFLAGS="-C relocation-model=static" cargo build --release \
#         --target x86_64-unknown-linux-musl
#     不依赖静态 glibc，Fedora 等未装 glibc-static 的发行版亦可静态链接；
#   - 宿主机 glibc（本地默认，无需额外 target）：
#       RUSTFLAGS="-C target-feature=+crt-static -C relocation-model=static" cargo build --release
# 选择逻辑见 link-mode.sh（musl 优先，否则 crt-static）。
#
# 使用：./build.sh [--musl]
set -euo pipefail
cd "$(dirname "$0")"
# shellcheck source=link-mode.sh
source ./link-mode.sh

mkdir -p build

mode="$(vault_link_mode)"
if [ "${1:-}" = "--musl" ]; then
  mode="musl"
fi

if [ "$mode" = "musl" ]; then
  TARGET=x86_64-unknown-linux-musl
  RUSTFLAGS="-C relocation-model=static" cargo build --release --target "$TARGET"
  cp "target/$TARGET/release/archoera-vault" "build/archoera-vault"
else
  RUSTFLAGS="-C target-feature=+crt-static -C relocation-model=static" cargo build --release
  cp target/release/archoera-vault build/archoera-vault
fi

echo "vault 产物就绪（静态链接）："
file build/archoera-vault
ls -lh build/archoera-vault
