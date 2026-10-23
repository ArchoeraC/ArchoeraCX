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

rem Every platform ships SQLitePCLRaw's bundled e_sqlite3 next to the module;
rem SqliteNativeLibrary loads that exact file by absolute path. The RID publish
rem normally flattens it into OUT_DIR, otherwise use the runtimes payload.
set "SQLITE_SOURCE=%OUT_DIR%\e_sqlite3.dll"
if not exist "%SQLITE_SOURCE%" set "SQLITE_SOURCE=%CD%\modules\business\Archoera.Exports\bin\Release\net10.0\win-x64\publish\runtimes\win-x64\native\e_sqlite3.dll"
if not exist "%SQLITE_SOURCE%" set "SQLITE_SOURCE=%CD%\modules\business\Archoera.Exports\bin\Release\net10.0\win-x64\runtimes\win-x64\native\e_sqlite3.dll"
if not exist "%SQLITE_SOURCE%" (
  echo error: bundled SQLite payload e_sqlite3.dll not found for win-x64 1>&2
  echo        looked in %OUT_DIR% and the Archoera.Exports publish directory. 1>&2
  exit /b 1
)

copy /y "%SQLITE_SOURCE%" "%CD%\build\native\e_sqlite3.dll" >nul
if errorlevel 1 exit /b 1
echo SQLite: %CD%\build\native\e_sqlite3.dll

echo Built: %FINAL_PATH%
