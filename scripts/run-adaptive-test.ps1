param(
    [string]$BuildRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "test-build"),
    [string]$TestProfileRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "test-profile")
)

$ErrorActionPreference = "Stop"
$executable = Join-Path $BuildRoot "Potato Launcher.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Test build not found: $executable"
}
if (-not (Test-Path -LiteralPath $TestProfileRoot)) {
    throw "Test profile not found. Run scripts\prepare-adaptive-test.ps1 first."
}

Start-Process -FilePath $executable -ArgumentList @("--data-dir", $TestProfileRoot)
