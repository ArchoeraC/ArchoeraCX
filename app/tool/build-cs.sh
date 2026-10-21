#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

case "$(uname -m)" in
  arm64|aarch64) DEFAULT_ARCH="arm64" ;;
  armv7l|armv6l) DEFAULT_ARCH="arm" ;;
  *) DEFAULT_ARCH="x64" ;;
esac

case "$(uname -s)" in
  Darwin)
    RID="${RID:-osx-${DEFAULT_ARCH}}"
    OUTPUT_NAME="archoera_cs.dylib"
    ;;
  *)
    RID="${RID:-linux-${DEFAULT_ARCH}}"
    OUTPUT_NAME="archoera_cs.so"
    ;;
esac

OUT_DIR="$(pwd)/build/native/cs"
FINAL_PATH="$(pwd)/build/native/${OUTPUT_NAME}"
PROJECT="modules/business/Archoera.Exports/Archoera.Exports.csproj"

CHINA_BUILD_VALUE="${ARCHOERACX_CHINA_BUILD:-false}"
if [[ "${CHINA_BUILD_VALUE}" == "1" ]]; then
  CHINA_BUILD_VALUE=true
fi

mkdir -p "${OUT_DIR}" "$(dirname "${FINAL_PATH}")"

dotnet publish "${PROJECT}" \
  -c Release \
  -r "${RID}" \
  -p:PublishAot=true \
  -p:NativeLib=Shared \
  -p:MicrosoftClientId="${ARCHOERACX_MICROSOFT_CLIENT_ID:-}" \
  -p:ChinaBuild="${CHINA_BUILD_VALUE}" \
  -o "${OUT_DIR}" \
  --nologo \
  "$@"

PRODUCED=""
for candidate in "${OUT_DIR}/${OUTPUT_NAME}" "${OUT_DIR}/lib${OUTPUT_NAME}"; do
  if [[ -f "${candidate}" ]]; then
    PRODUCED="${candidate}"
    break
  fi
done

if [[ -z "${PRODUCED}" ]]; then
  echo "error: NativeAOT output not found in ${OUT_DIR}" >&2
  ls -la "${OUT_DIR}" >&2
  exit 1
fi

install -m 0755 "${PRODUCED}" "${FINAL_PATH}"

# Linux/macOS use the system SQLite loaded by the process (no second native
# copy). Windows has no system SQLite, so keep the bundled DLL next to the AOT
# module. Flutter itself never opens SQLite.
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    if [[ -f "${OUT_DIR}/e_sqlite3.dll" ]]; then
      install -m 0755 "${OUT_DIR}/e_sqlite3.dll" "$(pwd)/build/native/e_sqlite3.dll"
      echo "SQLite: build/native/e_sqlite3.dll"
    fi
    ;;
  *)
    rm -f "$(pwd)/build/native/libe_sqlite3.so" "$(pwd)/build/native/libe_sqlite3.dylib"
    ;;
esac

echo "Built: ${FINAL_PATH}"
