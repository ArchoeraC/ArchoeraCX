@echo off
setlocal

cd /d "%~dp0.."

cmake -S .. -B build/native -DCMAKE_BUILD_TYPE=Release
if errorlevel 1 exit /b 1

cmake --build build/native --config Release
if errorlevel 1 exit /b 1

echo Built: %CD%\build\native\archoera_core.dll
