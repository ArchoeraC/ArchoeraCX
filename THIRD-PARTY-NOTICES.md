# Third-Party Notices / 第三方许可清单

**Project:** ArchoeraCX  
**Project license:** AGPL-3.0-only, see [`LICENSE`](LICENSE).  
**Release channel:** GitHub pre-releases only.  
**Inventory date:** 2026-10-21

This file is an inventory of third-party components used by the ArchoeraCX desktop build. It does not replace the full license text shipped by each upstream package. Before publishing a GitHub Release, include this file, `app/assets/fonts/OFL.txt`, and the loader-icon license files in the release archive.

The application icon, the default offline skin, and ArchoeraCX-owned source code are original project assets and are distributed under AGPL-3.0-only. No Mojang, Microsoft, Microsoft Windows, or Minecraft logo/asset is included in the application icon.

## 1. Application assets

| Component | Version/source | License | Notes |
|---|---|---|---|
| ArchoeraCX application icon | `app/assets/branding/archoera_icon.svg` | AGPL-3.0-only | Original flat viewfinder frame with an amber diamond core; no Mojang/Microsoft/Windows elements |
| Default offline skin | `app/assets/skins/default-offline.png` | AGPL-3.0-only | Original neutral local character; not a Mojang asset |
| Noto Sans SC Medium | `app/assets/fonts/NotoSansSC-Medium.otf` | OFL-1.1 | License text: `app/assets/fonts/OFL.txt` |
| Fabric loader icon | `app/assets/icons/loaders/fabric.png` | Apache-2.0 | Source/attribution: `app/assets/icons/loaders/SOURCES.md` |
| Quilt loader icon | `app/assets/icons/loaders/quilt.png` | Apache-2.0 | Source/attribution: `app/assets/icons/loaders/SOURCES.md` |
| Forge loader icon | `app/assets/icons/loaders/forge.png` | LGPL-2.1 | Source/attribution: `app/assets/icons/loaders/SOURCES.md` |
| NeoForge loader icon | `app/assets/icons/loaders/neoforge.png` | LGPL-2.1 | Source/attribution: `app/assets/icons/loaders/SOURCES.md` |
| Vanilla loader illustration | `app/assets/icons/loaders/vanilla.png` | Public domain / trademark notice | Unofficial grass-block illustration; see `SOURCES.md` |

## 2. Flutter / Dart packages

Versions are locked by `app/pubspec.lock`. The Flutter SDK and Dart SDK are BSD-3-Clause; the Flutter engine is distributed under its upstream BSD-style license and notices bundled by Flutter.

| Package | Version | Scope | License | Upstream |
|---|---:|---|---|---|
| `args` | 2.7.0 | transitive | BSD-3-Clause | https://pub.dev/packages/args/versions/2.7.0 |
| `async` | 2.13.1 | transitive | BSD-3-Clause | https://pub.dev/packages/async/versions/2.13.1 |
| `boolean_selector` | 2.1.2 | transitive | BSD-3-Clause | https://pub.dev/packages/boolean_selector/versions/2.1.2 |
| `characters` | 1.4.1 | transitive | BSD-3-Clause | https://pub.dev/packages/characters/versions/1.4.1 |
| `clock` | 1.1.3 | transitive | Apache-2.0 | https://pub.dev/packages/clock/versions/1.1.3 |
| `code_assets` | 2.1.0 | transitive | BSD-3-Clause | https://pub.dev/packages/code_assets/versions/2.1.0 |
| `collection` | 1.19.1 | transitive | BSD-3-Clause | https://pub.dev/packages/collection/versions/1.19.1 |
| `cross_file` | 0.3.5+5 | transitive | BSD-3-Clause | https://pub.dev/packages/cross_file/versions/0.3.5+5 |
| `crypto` | 3.0.7 | transitive | BSD-3-Clause | https://pub.dev/packages/crypto/versions/3.0.7 |
| `cupertino_ui` | 1.1.3 | transitive | BSD-3-Clause | https://pub.dev/packages/cupertino_ui/versions/1.1.3 |
| `fake_async` | 1.3.3 | transitive | Apache-2.0 | https://pub.dev/packages/fake_async/versions/1.3.3 |
| `ffi` | 2.2.0 | runtime | BSD-3-Clause | https://pub.dev/packages/ffi/versions/2.2.0 |
| `file` | 7.0.1 | transitive | BSD-3-Clause | https://pub.dev/packages/file/versions/7.0.1 |
| `file_selector` | 1.1.0 | runtime | BSD-3-Clause | https://pub.dev/packages/file_selector/versions/1.1.0 |
| `file_selector_android` | 0.5.2+11 | transitive | Apache-2.0 | https://pub.dev/packages/file_selector_android/versions/0.5.2+11 |
| `file_selector_ios` | 0.5.3+7 | transitive | BSD-3-Clause | https://pub.dev/packages/file_selector_ios/versions/0.5.3+7 |
| `file_selector_linux` | 0.9.4+1 | transitive | BSD-3-Clause | https://pub.dev/packages/file_selector_linux/versions/0.9.4+1 |
| `file_selector_macos` | 0.9.5+2 | transitive | BSD-3-Clause | https://pub.dev/packages/file_selector_macos/versions/0.9.5+2 |
| `file_selector_platform_interface` | 2.7.0 | transitive | BSD-3-Clause | https://pub.dev/packages/file_selector_platform_interface/versions/2.7.0 |
| `file_selector_web` | 0.9.5 | transitive | BSD-3-Clause | https://pub.dev/packages/file_selector_web/versions/0.9.5 |
| `file_selector_windows` | 0.9.3+6 | transitive | BSD-3-Clause | https://pub.dev/packages/file_selector_windows/versions/0.9.3+6 |
| `fixnum` | 1.1.1 | transitive | BSD-3-Clause | https://pub.dev/packages/fixnum/versions/1.1.1 |
| `flutter` | 0.0.0 | runtime | BSD-3-Clause | https://github.com/flutter/flutter |
| `flutter_lints` | 6.0.0 | dev | BSD-3-Clause | https://pub.dev/packages/flutter_lints/versions/6.0.0 |
| `flutter_localizations` | 0.0.0 | runtime | BSD-3-Clause | https://github.com/flutter/flutter |
| `flutter_riverpod` | 3.4.3 | runtime | MIT | https://pub.dev/packages/flutter_riverpod/versions/3.4.3 |
| `flutter_test` | 0.0.0 | dev | BSD-3-Clause | https://github.com/flutter/flutter |
| `flutter_web_plugins` | 0.0.0 | transitive | BSD-3-Clause | https://github.com/flutter/flutter |
| `go_router` | 18.1.0 | runtime | BSD-3-Clause | https://pub.dev/packages/go_router/versions/18.1.0 |
| `hooks` | 2.2.0 | transitive | BSD-3-Clause | https://pub.dev/packages/hooks/versions/2.2.0 |
| `http` | 1.6.0 | transitive | BSD-3-Clause | https://pub.dev/packages/http/versions/1.6.0 |
| `http_parser` | 4.1.2 | transitive | BSD-3-Clause | https://pub.dev/packages/http_parser/versions/4.1.2 |
| `intl` | 0.20.3 | runtime | BSD-3-Clause | https://pub.dev/packages/intl/versions/0.20.3 |
| `jni` | 1.1.0 | transitive | BSD-3-Clause | https://pub.dev/packages/jni/versions/1.1.0 |
| `jni_util` | 1.0.0 | transitive | BSD-3-Clause | https://pub.dev/packages/jni_util/versions/1.0.0 |
| `leak_tracker` | 11.0.2 | transitive | BSD-3-Clause | https://pub.dev/packages/leak_tracker/versions/11.0.2 |
| `leak_tracker_flutter_testing` | 3.0.10 | transitive | BSD-3-Clause | https://pub.dev/packages/leak_tracker_flutter_testing/versions/3.0.10 |
| `leak_tracker_testing` | 3.0.2 | transitive | BSD-3-Clause | https://pub.dev/packages/leak_tracker_testing/versions/3.0.2 |
| `lints` | 6.1.0 | transitive | BSD-3-Clause | https://pub.dev/packages/lints/versions/6.1.0 |
| `listen` | 1.0.1 | transitive | BSD-3-Clause | https://pub.dev/packages/listen/versions/1.0.1 |
| `logging` | 1.3.0 | transitive | BSD-3-Clause | https://pub.dev/packages/logging/versions/1.3.0 |
| `matcher` | 0.12.20 | transitive | BSD-3-Clause | https://pub.dev/packages/matcher/versions/0.12.20 |
| `material_color_utilities` | 0.13.0 | transitive | Apache-2.0 | https://pub.dev/packages/material_color_utilities/versions/0.13.0 |
| `material_ui` | 1.7.0 | transitive | BSD-3-Clause | https://pub.dev/packages/material_ui/versions/1.7.0 |
| `meta` | 1.19.0 | transitive | BSD-3-Clause | https://pub.dev/packages/meta/versions/1.19.0 |
| `objective_c` | 9.6.2 | transitive | BSD-3-Clause | https://pub.dev/packages/objective_c/versions/9.6.2 |
| `package_config` | 3.0.0 | transitive | BSD-3-Clause | https://pub.dev/packages/package_config/versions/3.0.0 |
| `path` | 1.9.1 | transitive | BSD-3-Clause | https://pub.dev/packages/path/versions/1.9.1 |
| `path_provider_linux` | 2.2.2 | transitive | BSD-3-Clause | https://pub.dev/packages/path_provider_linux/versions/2.2.2 |
| `path_provider_platform_interface` | 2.1.3 | transitive | BSD-3-Clause | https://pub.dev/packages/path_provider_platform_interface/versions/2.1.3 |
| `path_provider_windows` | 2.3.0 | transitive | BSD-3-Clause | https://pub.dev/packages/path_provider_windows/versions/2.3.0 |
| `platform` | 3.2.0 | transitive | BSD-3-Clause | https://pub.dev/packages/platform/versions/3.2.0 |
| `plugin_platform_interface` | 2.1.8 | transitive | BSD-3-Clause | https://pub.dev/packages/plugin_platform_interface/versions/2.1.8 |
| `pub_semver` | 2.2.1 | transitive | BSD-3-Clause | https://pub.dev/packages/pub_semver/versions/2.2.1 |
| `record_use` | 1.1.1 | transitive | BSD-3-Clause | https://pub.dev/packages/record_use/versions/1.1.1 |
| `riverpod` | 3.4.3 | transitive | MIT | https://pub.dev/packages/riverpod/versions/3.4.3 |
| `shared_preferences` | 2.5.6 | runtime | BSD-3-Clause | https://pub.dev/packages/shared_preferences/versions/2.5.6 |
| `shared_preferences_android` | 2.5.1 | transitive | BSD-3-Clause | https://pub.dev/packages/shared_preferences_android/versions/2.5.1 |
| `shared_preferences_foundation` | 2.6.0 | transitive | BSD-3-Clause | https://pub.dev/packages/shared_preferences_foundation/versions/2.6.0 |
| `shared_preferences_linux` | 2.4.1 | transitive | BSD-3-Clause | https://pub.dev/packages/shared_preferences_linux/versions/2.4.1 |
| `shared_preferences_platform_interface` | 2.4.2 | transitive | BSD-3-Clause | https://pub.dev/packages/shared_preferences_platform_interface/versions/2.4.2 |
| `shared_preferences_web` | 2.4.3 | transitive | BSD-3-Clause | https://pub.dev/packages/shared_preferences_web/versions/2.4.3 |
| `shared_preferences_windows` | 2.4.1 | transitive | BSD-3-Clause | https://pub.dev/packages/shared_preferences_windows/versions/2.4.1 |
| `sky_engine` | 0.0.0 | transitive | MIT | https://pub.dev/packages/sky_engine/versions/0.0.0 |
| `source_span` | 1.10.2 | transitive | BSD-3-Clause | https://pub.dev/packages/source_span/versions/1.10.2 |
| `stack_trace` | 1.12.2 | transitive | BSD-3-Clause | https://pub.dev/packages/stack_trace/versions/1.12.2 |
| `state_notifier` | 1.0.0 | transitive | MIT | https://pub.dev/packages/state_notifier/versions/1.0.0 |
| `stream_channel` | 2.1.4 | transitive | BSD-3-Clause | https://pub.dev/packages/stream_channel/versions/2.1.4 |
| `string_scanner` | 1.4.1 | transitive | BSD-3-Clause | https://pub.dev/packages/string_scanner/versions/1.4.1 |
| `term_glyph` | 1.2.2 | transitive | BSD-3-Clause | https://pub.dev/packages/term_glyph/versions/1.2.2 |
| `test_api` | 0.7.12 | transitive | BSD-3-Clause | https://pub.dev/packages/test_api/versions/0.7.12 |
| `typed_data` | 1.4.0 | transitive | BSD-3-Clause | https://pub.dev/packages/typed_data/versions/1.4.0 |
| `url_launcher` | 6.3.3 | runtime | BSD-3-Clause | https://pub.dev/packages/url_launcher/versions/6.3.3 |
| `url_launcher_android` | 6.3.33 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_android/versions/6.3.33 |
| `url_launcher_ios` | 6.4.2 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_ios/versions/6.4.2 |
| `url_launcher_linux` | 3.2.3 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_linux/versions/3.2.3 |
| `url_launcher_macos` | 3.2.6 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_macos/versions/3.2.6 |
| `url_launcher_platform_interface` | 2.3.2 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_platform_interface/versions/2.3.2 |
| `url_launcher_web` | 2.4.3 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_web/versions/2.4.3 |
| `url_launcher_windows` | 3.1.6 | transitive | BSD-3-Clause | https://pub.dev/packages/url_launcher_windows/versions/3.1.6 |
| `uuid` | 4.6.0 | transitive | MIT | https://pub.dev/packages/uuid/versions/4.6.0 |
| `vector_math` | 2.4.3 | transitive | BSD-3-Clause | https://pub.dev/packages/vector_math/versions/2.4.3 |
| `vm_service` | 15.4.0 | transitive | BSD-3-Clause | https://pub.dev/packages/vm_service/versions/15.4.0 |
| `web` | 1.1.1 | transitive | BSD-3-Clause | https://pub.dev/packages/web/versions/1.1.1 |
| `xdg_directories` | 1.1.0 | transitive | BSD-3-Clause | https://pub.dev/packages/xdg_directories/versions/1.1.0 |
| `yaml` | 3.1.4 | transitive | MIT | https://pub.dev/packages/yaml/versions/3.1.4 |
| Flutter SDK | 3.47.7 | runtime | BSD-3-Clause | https://github.com/flutter/flutter |
| Dart SDK | 3.13.5 | runtime | BSD-3-Clause | https://github.com/dart-lang/sdk |

## 3. .NET / native runtime components

| Component | Version | License | Notes |
|---|---:|---|---|
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 | https://www.nuget.org/packages/SQLitePCLRaw.core |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 | Windows bundled native provider |
| SQLitePCLRaw.provider.sqlite3 | 2.1.12 | Apache-2.0 | Linux/macOS system SQLite provider |
| SQLite (e_sqlite3) | system/bundled | Public domain | https://www.sqlite.org/copyright.html |
| .NET runtime and NativeAOT | 10.0.x | MIT | https://github.com/dotnet/runtime |

## 4. Rust crates (runtime closure)

Generated from `app/modules/downloader/Cargo.lock` and `cargo metadata`; dev-only test crates are excluded from this closure. License expressions are the SPDX expressions declared by each crate; consult each upstream package for the full text.

| Crate | Version | License expression | Upstream |
|---|---:|---|---|
| `atomic-waker` | 1.1.2 | Apache-2.0 OR MIT | https://github.com/smol-rs/atomic-waker |
| `aws-lc-rs` | 1.18.2 | ISC AND (Apache-2.0 OR ISC) | https://github.com/aws/aws-lc-rs |
| `aws-lc-sys` | 0.46.0 | ISC AND (Apache-2.0 OR ISC) AND Apache-2.0 AND MIT AND BSD-3-Clause AND (Apache-2.0 OR ISC OR MIT) AND (Apache-2.0 OR ISC OR MIT-0) | https://github.com/aws/aws-lc-rs |
| `base64` | 0.23.1 | MIT OR Apache-2.0 | https://github.com/marshallpierce/rust-base64 |
| `bitflags` | 2.13.2 | MIT OR Apache-2.0 | https://github.com/bitflags/bitflags |
| `block-buffer` | 0.10.4 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils |
| `bumpalo` | 3.20.3 | MIT OR Apache-2.0 | https://github.com/fitzgen/bumpalo |
| `bytes` | 1.12.1 | MIT | https://github.com/tokio-rs/bytes |
| `cc` | 1.9.0 | MIT OR Apache-2.0 | https://github.com/rust-lang/cc-rs |
| `cfg-if` | 1.0.5 | MIT OR Apache-2.0 | https://github.com/rust-lang/cfg-if |
| `cfg_aliases` | 0.2.2 | MIT | https://github.com/katharostech/cfg_aliases |
| `chacha20` | 0.10.2 | MIT OR Apache-2.0 | https://github.com/RustCrypto/stream-ciphers |
| `cmake` | 0.1.58 | MIT OR Apache-2.0 | https://github.com/rust-lang/cmake-rs |
| `combine` | 4.6.9 | MIT | https://github.com/Marwes/combine |
| `core-foundation` | 0.10.1 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs |
| `core-foundation` | 0.9.4 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs |
| `core-foundation-sys` | 0.8.7 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs |
| `cpufeatures` | 0.2.17 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils |
| `cpufeatures` | 0.3.1 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils |
| `crypto-common` | 0.1.7 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits |
| `digest` | 0.10.7 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits |
| `displaydoc` | 0.2.7 | MIT OR Apache-2.0 | https://github.com/yaahc/displaydoc |
| `dunce` | 1.0.5 | CC0-1.0 OR MIT-0 OR Apache-2.0 | https://gitlab.com/kornelski/dunce |
| `encoding_rs` | 0.8.35 | (Apache-2.0 OR MIT) AND BSD-3-Clause | https://github.com/hsivonen/encoding_rs |
| `equivalent` | 1.0.3 | Apache-2.0 OR MIT | https://github.com/indexmap-rs/equivalent |
| `errno` | 0.3.14 | MIT OR Apache-2.0 | https://github.com/lambda-fairy/rust-errno |
| `find-msvc-tools` | 0.1.15 | MIT OR Apache-2.0 | https://github.com/rust-lang/cc-rs |
| `fnv` | 1.0.7 | Apache-2.0 / MIT | https://github.com/servo/rust-fnv |
| `form_urlencoded` | 1.2.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-url |
| `fs_extra` | 1.3.0 | MIT | https://github.com/webdesus/fs_extra |
| `futures-channel` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs |
| `futures-core` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs |
| `futures-macro` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs |
| `futures-sink` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs |
| `futures-task` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs |
| `futures-util` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs |
| `generic-array` | 0.14.7 | MIT | https://github.com/fizyk20/generic-array.git |
| `getrandom` | 0.2.17 | MIT OR Apache-2.0 | https://github.com/rust-random/getrandom |
| `getrandom` | 0.4.3 | MIT OR Apache-2.0 | https://github.com/rust-random/getrandom |
| `h2` | 0.4.20 | MIT | https://github.com/hyperium/h2 |
| `hashbrown` | 0.17.1 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown |
| `http` | 1.5.0 | MIT OR Apache-2.0 | https://github.com/hyperium/http |
| `http-body` | 1.1.0 | MIT | https://github.com/hyperium/http-body |
| `http-body-util` | 0.1.5 | MIT | https://github.com/hyperium/http-body |
| `httparse` | 1.10.1 | MIT OR Apache-2.0 | https://github.com/seanmonstar/httparse |
| `hyper` | 1.12.0 | MIT | https://github.com/hyperium/hyper |
| `hyper-rustls` | 0.27.10 | Apache-2.0 OR ISC OR MIT | https://github.com/rustls/hyper-rustls |
| `hyper-util` | 0.1.21 | MIT | https://github.com/hyperium/hyper-util |
| `icu_collections` | 2.1.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `icu_locale_core` | 2.1.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `icu_normalizer` | 2.1.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `icu_normalizer_data` | 2.1.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `icu_properties` | 2.1.2 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `icu_properties_data` | 2.1.2 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `icu_provider` | 2.1.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `idna` | 1.1.0 | MIT OR Apache-2.0 | https://github.com/servo/rust-url/ |
| `idna_adapter` | 1.2.1 | Apache-2.0 OR MIT | https://github.com/hsivonen/idna_adapter |
| `indexmap` | 2.14.2 | Apache-2.0 OR MIT | https://github.com/indexmap-rs/indexmap |
| `ipnet` | 2.12.2 | MIT OR Apache-2.0 | https://github.com/krisprice/ipnet |
| `itoa` | 1.0.18 | MIT OR Apache-2.0 | https://github.com/dtolnay/itoa |
| `jni` | 0.22.4 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-rs |
| `jni-macros` | 0.22.4 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-rs |
| `jni-sys` | 0.4.1 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-sys |
| `jni-sys-macros` | 0.4.1 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-sys |
| `jobserver` | 0.1.35 | MIT OR Apache-2.0 | https://github.com/rust-lang/jobserver-rs |
| `js-sys` | 0.3.106 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/js-sys |
| `libc` | 0.2.191 | MIT OR Apache-2.0 | https://github.com/rust-lang/libc |
| `litemap` | 0.8.3 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `lock_api` | 0.4.14 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot |
| `log` | 0.4.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/log |
| `lru-slab` | 0.1.3 | MIT OR Apache-2.0 OR Zlib | https://github.com/Ralith/lru-slab |
| `memchr` | 2.8.3 | Unlicense OR MIT | https://github.com/BurntSushi/memchr |
| `mime` | 0.3.17 | MIT OR Apache-2.0 | https://github.com/hyperium/mime |
| `mio` | 1.2.4 | MIT | https://github.com/tokio-rs/mio |
| `once_cell` | 1.21.4 | MIT OR Apache-2.0 | https://github.com/matklad/once_cell |
| `openssl-probe` | 0.2.1 | MIT OR Apache-2.0 | https://github.com/rustls/openssl-probe |
| `parking_lot` | 0.12.5 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot |
| `parking_lot_core` | 0.9.12 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot |
| `percent-encoding` | 2.3.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-url/ |
| `pin-project-lite` | 0.2.17 | Apache-2.0 OR MIT | https://github.com/taiki-e/pin-project-lite |
| `pkg-config` | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/pkg-config-rs |
| `potential_utf` | 0.1.6 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `proc-macro2` | 1.0.107 | MIT OR Apache-2.0 | https://github.com/dtolnay/proc-macro2 |
| `quinn` | 0.11.12 | MIT OR Apache-2.0 | https://github.com/quinn-rs/quinn |
| `quinn-proto` | 0.11.19 | MIT OR Apache-2.0 | https://github.com/quinn-rs/quinn |
| `quinn-udp` | 0.5.16 | MIT OR Apache-2.0 | https://github.com/quinn-rs/quinn |
| `quote` | 1.0.47 | MIT OR Apache-2.0 | https://github.com/dtolnay/quote |
| `r-efi` | 6.0.0 | MIT OR Apache-2.0 OR LGPL-2.1-or-later | https://github.com/r-efi/r-efi |
| `rand` | 0.10.3 | MIT OR Apache-2.0 | https://github.com/rust-random/rand |
| `rand_core` | 0.10.1 | MIT OR Apache-2.0 | https://github.com/rust-random/rand_core |
| `rand_pcg` | 0.10.2 | MIT OR Apache-2.0 | https://github.com/rust-random/rngs |
| `redox_syscall` | 0.5.18 | MIT | https://gitlab.redox-os.org/redox-os/syscall |
| `reqwest` | 0.13.5 | MIT OR Apache-2.0 | https://github.com/seanmonstar/reqwest |
| `ring` | 0.17.14 | Apache-2.0 AND ISC | https://github.com/briansmith/ring |
| `rustc-hash` | 2.1.3 | Apache-2.0 OR MIT | https://github.com/rust-lang/rustc-hash |
| `rustc_version` | 0.4.1 | MIT OR Apache-2.0 | https://github.com/djc/rustc-version-rs |
| `rustls` | 0.23.46 | Apache-2.0 OR ISC OR MIT | https://github.com/rustls/rustls |
| `rustls-native-certs` | 0.8.5 | Apache-2.0 OR ISC OR MIT | https://github.com/rustls/rustls-native-certs |
| `rustls-pki-types` | 1.15.1 | MIT OR Apache-2.0 | https://github.com/rustls/pki-types |
| `rustls-platform-verifier` | 0.7.1 | MIT OR Apache-2.0 | https://github.com/rustls/rustls-platform-verifier |
| `rustls-platform-verifier-android` | 0.2.0 | MIT OR Apache-2.0 | https://github.com/rustls/rustls-platform-verifier |
| `rustls-webpki` | 0.103.16 | ISC | https://github.com/rustls/webpki |
| `rustversion` | 1.0.23 | MIT OR Apache-2.0 | https://github.com/dtolnay/rustversion |
| `same-file` | 1.0.6 | Unlicense/MIT | https://github.com/BurntSushi/same-file |
| `schannel` | 0.1.30 | MIT | https://github.com/steffengy/schannel-rs |
| `scopeguard` | 1.2.0 | MIT OR Apache-2.0 | https://github.com/bluss/scopeguard |
| `security-framework` | 3.7.0 | MIT OR Apache-2.0 | https://github.com/kornelski/rust-security-framework |
| `security-framework-sys` | 2.17.0 | MIT OR Apache-2.0 | https://github.com/kornelski/rust-security-framework |
| `semver` | 1.0.28 | MIT OR Apache-2.0 | https://github.com/dtolnay/semver |
| `serde` | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde |
| `serde_core` | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde |
| `serde_derive` | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde |
| `serde_json` | 1.0.154 | MIT OR Apache-2.0 | https://github.com/serde-rs/json |
| `sha1` | 0.10.7 | MIT OR Apache-2.0 | https://github.com/RustCrypto/hashes |
| `sha2` | 0.10.9 | MIT OR Apache-2.0 | https://github.com/RustCrypto/hashes |
| `shlex` | 2.0.1 | MIT OR Apache-2.0 | https://github.com/comex/rust-shlex |
| `signal-hook-registry` | 1.4.8 | MIT OR Apache-2.0 | https://github.com/vorner/signal-hook |
| `simd_cesu8` | 1.2.0 | Apache-2.0 OR MIT | https://github.com/seancroach/simd_cesu8 |
| `simdutf8` | 0.1.5 | MIT OR Apache-2.0 | https://github.com/rusticstuff/simdutf8 |
| `slab` | 0.4.12 | MIT | https://github.com/tokio-rs/slab |
| `smallvec` | 1.16.3 | MIT OR Apache-2.0 | https://github.com/servo/rust-smallvec |
| `socket2` | 0.6.5 | MIT OR Apache-2.0 | https://github.com/rust-lang/socket2 |
| `stable_deref_trait` | 1.2.1 | MIT OR Apache-2.0 | https://github.com/storyyeller/stable_deref_trait |
| `subtle` | 2.6.1 | BSD-3-Clause | https://github.com/dalek-cryptography/subtle |
| `syn` | 2.0.119 | MIT OR Apache-2.0 | https://github.com/dtolnay/syn |
| `syn` | 3.0.9 | MIT OR Apache-2.0 | https://github.com/dtolnay/syn |
| `sync_wrapper` | 1.0.2 | Apache-2.0 | https://github.com/Actyx/sync_wrapper |
| `synstructure` | 0.14.0 | MIT | https://github.com/mystor/synstructure |
| `system-configuration` | 0.7.0 | MIT OR Apache-2.0 | https://github.com/mullvad/system-configuration-rs |
| `system-configuration-sys` | 0.6.0 | MIT OR Apache-2.0 | https://github.com/mullvad/system-configuration-rs |
| `thiserror` | 2.0.21 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror |
| `thiserror-impl` | 2.0.21 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror |
| `tinystr` | 0.8.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `tinyvec` | 1.13.3 | Zlib OR Apache-2.0 OR MIT | https://github.com/Lokathor/tinyvec |
| `tokio` | 1.54.0 | MIT | https://github.com/tokio-rs/tokio |
| `tokio-macros` | 2.7.2 | MIT | https://github.com/tokio-rs/tokio |
| `tokio-rustls` | 0.26.6 | MIT OR Apache-2.0 | https://github.com/rustls/tokio-rustls |
| `tokio-util` | 0.7.20 | MIT | https://github.com/tokio-rs/tokio |
| `tower` | 0.5.3 | MIT | https://github.com/tower-rs/tower |
| `tower-http` | 0.6.11 | MIT | https://github.com/tower-rs/tower-http |
| `tower-layer` | 0.3.3 | MIT | https://github.com/tower-rs/tower |
| `tower-service` | 0.3.3 | MIT | https://github.com/tower-rs/tower |
| `tracing` | 0.1.44 | MIT | https://github.com/tokio-rs/tracing |
| `tracing-core` | 0.1.36 | MIT | https://github.com/tokio-rs/tracing |
| `try-lock` | 0.2.5 | MIT | https://github.com/seanmonstar/try-lock |
| `typenum` | 1.20.1 | MIT OR Apache-2.0 | https://github.com/paholg/typenum |
| `unicode-ident` | 1.0.27 | (MIT OR Apache-2.0) AND Unicode-3.0 | https://github.com/dtolnay/unicode-ident |
| `untrusted` | 0.9.0 | ISC | https://github.com/briansmith/untrusted |
| `url` | 2.5.8 | MIT OR Apache-2.0 | https://github.com/servo/rust-url |
| `utf8_iter` | 1.0.4 | Apache-2.0 OR MIT | https://github.com/hsivonen/utf8_iter |
| `version_check` | 0.9.5 | MIT/Apache-2.0 | https://github.com/SergioBenitez/version_check |
| `walkdir` | 2.5.0 | Unlicense/MIT | https://github.com/BurntSushi/walkdir |
| `want` | 0.3.2 | MIT | https://github.com/seanmonstar/want |
| `wasi` | 0.11.1+wasi-snapshot-preview1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | https://github.com/bytecodealliance/wasi |
| `wasm-bindgen` | 0.2.129 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen |
| `wasm-bindgen-futures` | 0.4.79 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/futures |
| `wasm-bindgen-macro` | 0.2.129 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/macro |
| `wasm-bindgen-macro-support` | 0.2.129 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/main/crates/macro-support |
| `wasm-bindgen-shared` | 0.2.129 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/shared |
| `web-sys` | 0.3.106 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/web-sys |
| `web-time` | 1.1.0 | MIT OR Apache-2.0 | https://github.com/daxpedda/web-time |
| `webpki-root-certs` | 1.0.10 | CDLA-Permissive-2.0 | https://github.com/rustls/webpki-roots |
| `winapi-util` | 0.1.11 | Unlicense OR MIT | https://github.com/BurntSushi/winapi-util |
| `windows-link` | 0.2.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows-registry` | 0.6.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows-result` | 0.4.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows-strings` | 0.5.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows-sys` | 0.52.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows-sys` | 0.61.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows-targets` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_aarch64_gnullvm` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_aarch64_msvc` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_i686_gnu` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_i686_gnullvm` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_i686_msvc` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_x86_64_gnu` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_x86_64_gnullvm` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `windows_x86_64_msvc` | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| `writeable` | 0.6.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `yoke` | 0.8.3 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `yoke-derive` | 0.8.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `zerofrom` | 0.1.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `zerofrom-derive` | 0.1.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `zeroize` | 1.9.1 | Apache-2.0 OR MIT | https://github.com/RustCrypto/utils |
| `zerotrie` | 0.2.5 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `zerovec` | 0.11.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `zerovec-derive` | 0.11.6 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| `zmij` | 1.0.23 | MIT | https://github.com/dtolnay/zmij |

## 5. System / dynamically linked components

- GTK 3 (Linux system package): LGPL-2.1-or-later; not bundled in the release archive.

- GNU libstdc++ / system C++ runtime (where dynamically linked): GCC Runtime Library Exception; not bundled in the release archive.

- Linux kernel, glibc and distribution-provided libraries are used under the host system licenses and are not redistributed by ArchoeraCX.


## 6. Updating this inventory

Run the release build from the tagged source, regenerate this file from `pubspec.lock`, `Cargo.lock`, and the project files, then attach it to the GitHub Release together with the source tag. Do not add private API keys, client IDs, tokens, or CI secrets to this file.

Third-party license texts are retained in their upstream source/package archives. The project does not relicense those components under AGPL-3.0-only.

