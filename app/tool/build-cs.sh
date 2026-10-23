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

# Public OAuth client ID. Never commit a real value; CI injects it from
# repository secrets and local builds pass it through the environment. The
# build turns it into an obfuscated blob, so the id never lands in obj/ or in
# the binary's string table.
#
# Local convenience: when the environment is empty, read the git-ignored
# `client_id.key` from the repository root (or `app/`). Editors and Windows
# tooling like to prepend a UTF-8 BOM or trailing CRLF, which would end up
# inside the blob and silently break sign-in, so strip both and validate the
# GUID shape before using it.
if [[ -z "${ARCHOERACX_MICROSOFT_CLIENT_ID:-}" ]]; then
  for candidate in "$(pwd)/../client_id.key" "$(pwd)/client_id.key"; do
    if [[ -f "${candidate}" ]]; then
      read -r ARCHOERACX_MICROSOFT_CLIENT_ID < <(
        tr -d '\r' < "${candidate}" | sed -e '1s/^\xEF\xBB\xBF//' -e 's/[[:space:]]//g'
      )
      export ARCHOERACX_MICROSOFT_CLIENT_ID
      echo "client id: read from ${candidate} (git-ignored)"
      break
    fi
  done
fi

if [[ -n "${ARCHOERACX_MICROSOFT_CLIENT_ID:-}" ]] &&
   [[ ! "${ARCHOERACX_MICROSOFT_CLIENT_ID}" =~ ^[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}$ ]]; then
  echo "error: ARCHOERACX_MICROSOFT_CLIENT_ID is not a GUID (length ${#ARCHOERACX_MICROSOFT_CLIENT_ID});" >&2
  echo "       expected the Application (client) ID, e.g. 00000000-0000-0000-0000-000000000000." >&2
  exit 1
fi

if [[ -z "${ARCHOERACX_MICROSOFT_CLIENT_ID:-}" ]]; then
  echo "warning: ARCHOERACX_MICROSOFT_CLIENT_ID is empty; Microsoft sign-in will" >&2
  echo "         return MICROSOFT_CLIENT_ID_MISSING and the About build marker stays hidden." >&2
else
  echo "client id: injected (obfuscated at build time, value not echoed)"
fi

OUT_DIR="$(pwd)/build/native/cs"
FINAL_PATH="$(pwd)/build/native/${OUTPUT_NAME}"
PROJECT="modules/business/Archoera.Exports/Archoera.Exports.csproj"

mkdir -p "${OUT_DIR}" "$(dirname "${FINAL_PATH}")"

dotnet publish "${PROJECT}" \
  -c Release \
  -r "${RID}" \
  -p:PublishAot=true \
  -p:NativeLib=Shared \
  -p:MicrosoftClientId="${ARCHOERACX_MICROSOFT_CLIENT_ID:-}" \
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
