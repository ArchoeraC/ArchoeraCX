# Package the Windows Flutter Release directory.
#
# Usage:
#   pwsh packaging/windows/package.ps1 -ReleaseDir app/build/windows/x64/runner/Release -Version 0.0.1+1
#
# Produces dist/ArchoeraCX-<version>-windows-<arch>.zip plus a .sha256 file.
# Native DLLs/exes are installed by app/windows/CMakeLists.txt from
# app/build/native; run tool/build-native.bat before `flutter build windows`.
param(
    [Parameter(Mandatory = $true)][string]$ReleaseDir,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Arch = 'x64',
    [string]$DistDir = ''
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$app = Join-Path $root 'app'
if ([string]::IsNullOrWhiteSpace($DistDir)) {
    $DistDir = Join-Path $root 'dist'
}
$ReleaseDir = (Resolve-Path $ReleaseDir).Path
$name = "ArchoeraCX-$Version-windows-$Arch"
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("archoera-win-package-" + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $work $name
$licenses = Join-Path $packageRoot 'licenses'

New-Item -ItemType Directory -Force -Path $licenses, $DistDir | Out-Null
try {
    Copy-Item -Path (Join-Path $ReleaseDir '*') -Destination $packageRoot -Recurse -Force
    Copy-Item -Path (Join-Path $root 'README.md'),
        (Join-Path $root 'LICENSE'),
        (Join-Path $root 'PRIVACY.md'),
        (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $packageRoot -Force
    Copy-Item -Path (Join-Path $app 'assets/fonts/OFL.txt'),
        (Join-Path $app 'assets/licenses/Tabler-Icons-MIT.txt'),
        (Join-Path $app 'assets/icons/loaders/LICENSE-APACHE-2.0.txt'),
        (Join-Path $app 'assets/icons/loaders/LICENSE-LGPL-2.1.txt') -Destination $licenses -Force
    Copy-Item -Path (Join-Path $app 'assets/branding/SOURCES.md') `
        -Destination (Join-Path $licenses 'branding-SOURCES.md') -Force
    Copy-Item -Path (Join-Path $app 'assets/icons/loaders/SOURCES.md') `
        -Destination (Join-Path $licenses 'loader-icons-SOURCES.md') -Force
    Copy-Item -Path (Join-Path $app 'assets/icons/ui/SOURCES.md') `
        -Destination (Join-Path $licenses 'ui-icons-SOURCES.md') -Force

    $zip = Join-Path $DistDir "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $packageRoot -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
    "$hash  $name.zip" | Set-Content -Encoding ascii (Join-Path $DistDir "$name.zip.sha256")

    Write-Host "Packaged: $zip"
    Get-Item $zip | Format-List Name, Length
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
