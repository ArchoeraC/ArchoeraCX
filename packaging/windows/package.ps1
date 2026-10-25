# Package the Windows Flutter Release directory.
#
# Usage:
#   pwsh packaging/windows/package.ps1 -ReleaseDir app/build/windows/x64/runner/Release `
#     -Version 0.0.1+1 [-Arch x64] [-Installer]
#
# The portable zip is always produced next to dist/. With -Installer, the same
# staged tree is compiled into an Inno Setup installer (per-user by default).
# Native DLLs/exes are installed by app/windows/CMakeLists.txt from
# app/build/native; run tool/build-native.bat before `flutter build windows`.
param(
    [Parameter(Mandatory = $true)][string]$ReleaseDir,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Arch = 'x64',
    [string]$DistDir = '',
    [switch]$Installer
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

function Write-Checksum([string]$Path) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
    $leaf = Split-Path -Leaf $Path
    "$hash  $leaf" | Set-Content -Encoding ascii (Join-Path $DistDir "$leaf.sha256")
}

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

    $commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { 'unknown' }
    $info = @(
        "ArchoeraCX $Version",
        "Version   : $Version",
        "Target    : windows-$Arch",
        "Built at  : $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))",
        "Commit    : $commit",
        "License   : AGPL-3.0-only (see LICENSE); third-party notices in THIRD-PARTY-NOTICES.md"
    )
    Set-Content -Encoding utf8 -Path (Join-Path $packageRoot 'BUILD-INFO.txt') -Value $info

    $zip = Join-Path $DistDir "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $packageRoot -DestinationPath $zip -CompressionLevel Optimal
    Write-Checksum $zip
    Write-Host "Packaged: $zip"

    if ($Installer) {
        $iscc = Get-Command iscc -ErrorAction SilentlyContinue
        if ($null -eq $iscc) {
            $candidate = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
            if (Test-Path $candidate) {
                $iscc = Get-Item $candidate
            }
        }
        if ($null -eq $iscc) {
            throw 'Inno Setup (ISCC.exe) not found; install it or omit -Installer.'
        }
        $exe = $iscc.Source
        if ($null -eq $exe) { $exe = $iscc.FullName }
        & $exe `
            "/DVersion=$Version" `
            "/DAppDir=$packageRoot" `
            "/DOutputDir=$DistDir" `
            (Join-Path $PSScriptRoot 'archoera_cx.iss')
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed with exit code $LASTEXITCODE"
        }
        $setup = Join-Path $DistDir "ArchoeraCX-$Version-windows-$Arch-setup.exe"
        if (-not (Test-Path $setup)) {
            throw "expected installer not found: $setup"
        }
        Write-Checksum $setup
        Write-Host "Packaged: $setup"
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
