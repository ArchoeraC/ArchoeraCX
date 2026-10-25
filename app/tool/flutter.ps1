# Invoke the Flutter tool without going through flutter.bat.
#
# On Windows, GitHub Actions cancels jobs by sending Ctrl+C. cmd.exe then asks
# "Terminate batch job (Y/N)?" for a running .bat file and can hang until the
# job timeout. Calling the same Dart snapshot directly avoids that wrapper.
#
# Usage (from the app/ directory):
#   pwsh -NoProfile -File tool/flutter.ps1 test
#   pwsh -NoProfile -File tool/flutter.ps1 build windows --release
$ErrorActionPreference = 'Stop'

$root = $env:FLUTTER_ROOT
if ([string]::IsNullOrWhiteSpace($root)) {
    $flutter = Get-Command flutter -ErrorAction SilentlyContinue
    if ($null -eq $flutter) {
        throw 'FLUTTER_ROOT is not set and flutter is not on PATH.'
    }
    $root = Split-Path -Parent (Split-Path -Parent $flutter.Source)
}

$dart = Join-Path $root 'bin\cache\dart-sdk\bin\dart.exe'
$snapshot = Join-Path $root 'bin\cache\flutter_tools.snapshot'

# subosito/flutter-action normally populates these during setup. If a cache is
# cold, run the official entry point once; it is short-lived and not the step
# that would be cancelled during the long test/build phases.
if (-not (Test-Path $snapshot)) {
    & (Join-Path $root 'bin\flutter.bat') --version | Out-Null
}
if (-not (Test-Path $dart) -or -not (Test-Path $snapshot)) {
    throw "Flutter cache is incomplete under $root"
}

& $dart $snapshot @args
exit $LASTEXITCODE
