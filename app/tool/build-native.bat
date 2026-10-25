@echo off
rem One-stop native build for Windows.
rem
rem CI and local development share this entry point; tool/build-native.sh is
rem the Linux/macOS equivalent. MicrosoftClientId stays in the environment
rem (never on the command line) exactly like build-cs.bat.
setlocal

cd /d "%~dp0.."

call tool\build-core.bat
if errorlevel 1 exit /b 1

call tool\build-cs.bat %*
if errorlevel 1 exit /b 1

call tool\build-dl.bat
if errorlevel 1 exit /b 1

call tool\build-vault.bat
if errorlevel 1 exit /b 1

echo Native build complete: %CD%\build\native
