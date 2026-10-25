@echo off
rem Build the Rust DownloadPlan executor and keep the CLI next to the core.
rem Mirrors tool/build-dl.sh for native Windows shells.
setlocal

cd /d "%~dp0.."

cargo build --release --locked --manifest-path modules\downloader\Cargo.toml
if errorlevel 1 exit /b 1

if not exist build\native mkdir build\native
copy /y modules\downloader\target\release\archoera_dl.dll build\native\archoera_dl.dll >nul
if errorlevel 1 exit /b 1
copy /y modules\downloader\target\release\archoera-dl-cli.exe build\native\archoera-dl.exe >nul
if errorlevel 1 exit /b 1

echo Built: %CD%\build\native\archoera_dl.dll
echo CLI:   %CD%\build\native\archoera-dl.exe
