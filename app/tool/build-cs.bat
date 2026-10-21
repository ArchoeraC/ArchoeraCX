@echo off
setlocal

cd /d "%~dp0.."

set "OUT_DIR=%CD%\build\native\cs"
set "FINAL_PATH=%CD%\build\native\archoera_cs.dll"
set "PROJECT=modules\business\Archoera.Exports\Archoera.Exports.csproj"

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
if not exist "%CD%\build\native" mkdir "%CD%\build\native"

dotnet publish "%PROJECT%" -c Release -r win-x64 -p:PublishAot=true -p:NativeLib=Shared -p:MicrosoftClientId="%ARCHOERACX_MICROSOFT_CLIENT_ID%" -o "%OUT_DIR%" --nologo %*
if errorlevel 1 exit /b 1

if not exist "%OUT_DIR%\archoera_cs.dll" (
  echo error: NativeAOT output not found in %OUT_DIR% 1>&2
  dir /b "%OUT_DIR%"
  exit /b 1
)

copy /y "%OUT_DIR%\archoera_cs.dll" "%FINAL_PATH%" >nul
if errorlevel 1 exit /b 1

rem Windows has no system SQLite; keep the bundled provider next to the module.
if exist "%OUT_DIR%\e_sqlite3.dll" (
  copy /y "%OUT_DIR%\e_sqlite3.dll" "%CD%\build\native\e_sqlite3.dll" >nul
  if errorlevel 1 exit /b 1
  echo SQLite: %CD%\build\native\e_sqlite3.dll
)

echo Built: %FINAL_PATH%
