#!/usr/bin/env bash
# ArchoeraMusic vault（Rust）静态链接方式选择（build.sh / build-test.sh 共用，source 使用）。
#
# 用法：source link-mode.sh; mode="$(vault_link_mode)"
# 返回（stdout）：
#   musl —— 已安装 x86_64-unknown-linux-musl（CI 由 build-linux action 预装）：不依赖
#           静态 glibc，Fedora 等未装 glibc-static 的发行版亦可静态链接；
#   crt  —— 宿主机 glibc + RUSTFLAGS="-C target-feature=+crt-static"（本地默认，无需
#           额外 target；`file` 报 "statically linked"）。
# 两种模式均产出静态单文件（musl 侧另加 -C relocation-model=static 以得到非 PIE 的
# "statically linked"，与 crt 侧一致）。强制 musl：环境变量 ARCHOERA_VAULT_MUSL=1。
vault_link_mode() {
  if [ "${ARCHOERA_VAULT_MUSL:-}" = "1" ]; then
    printf 'musl\n'
  elif command -v rustup >/dev/null 2>&1 \
       && rustup target list --installed 2>/dev/null | grep -qx 'x86_64-unknown-linux-musl'; then
    printf 'musl\n'
  else
    printf 'crt\n'
  fi
}
