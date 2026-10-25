@echo off
rem Build the Rust credential vault as a single self-contained executable.
rem Mirrors tool/build-vault.sh for native Windows shells.
setlocal

cd /d "%~dp0.."

if not exist build\native mkdir build\native
cargo build --release --locked --manifest-path core\vault-rs\Cargo.toml
if errorlevel 1 exit /b 1

copy /y core\vault-rs\target\release\archoera-vault.exe build\native\archoera-vault.exe >nul
if errorlevel 1 exit /b 1

echo Built: %CD%\build\native\archoera-vault.exe
