#!/usr/bin/env bash
# ArchoeraMusic vault Rust 测试构建（vault_testing feature，幂等）。
#
# 产物：vault-rs/build/archoera-vault-test
#   与生产 build.sh 唯一区别：启用 `vault_testing` feature → 构建标记输出 TEST
#   （main.rs MARKER），供握手指纹断言与生产/测试判别使用。存储后端与生产一致。
#   仅测试/CI 使用，绝不发布；生产二进制由 build.sh 产出。
#   静态链接方式与 build.sh 一致（musl 优先，否则宿主机 crt-static；见 link-mode.sh）。
#
# 使用：./build-test.sh
set -euo pipefail
cd "$(dirname "$0")"
# shellcheck source=link-mode.sh
source ./link-mode.sh

mkdir -p build

if [ "$(vault_link_mode)" = "musl" ]; then
  TARGET=x86_64-unknown-linux-musl
  RUSTFLAGS="-C relocation-model=static" cargo build --release --target "$TARGET" --features vault_testing
  cp "target/$TARGET/release/archoera-vault" build/archoera-vault-test
else
  RUSTFLAGS="-C target-feature=+crt-static -C relocation-model=static" cargo build --release --features vault_testing
  cp target/release/archoera-vault build/archoera-vault-test
fi

echo "vault 测试产物就绪："
ls -lh build/archoera-vault-test
