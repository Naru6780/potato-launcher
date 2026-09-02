param(
    [string]$SourceDataRoot = (Join-Path $env:APPDATA "Potato Launcher"),
    [string]$TestProfileRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "test-profile")
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testRoot = [System.IO.Path]::GetFullPath($TestProfileRoot)
if (-not $testRoot.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The test profile must stay under the source checkout: $repoRoot"
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
foreach ($fileName in @("settings.json", "accountList.json", "band.json", "optimizer.json")) {
    $sourcePath = Join-Path $SourceDataRoot $fileName
    if (Test-Path -LiteralPath $sourcePath) {
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $testRoot $fileName) -Force
    }
}

$optimizerPath = Join-Path $testRoot "optimizer.json"
$optimizer = if (Test-Path -LiteralPath $optimizerPath) {
    Get-Content -Raw -LiteralPath $optimizerPath | ConvertFrom-Json
} else {
    [pscustomobject]@{}
}
$optimizer | Add-Member -NotePropertyName cpuPreviewOnly -NotePropertyValue $true -Force
$optimizer | Add-Member -NotePropertyName cpuAssignmentMode -NotePropertyValue "AdaptiveSharedPools" -Force
$optimizer | Add-Member -NotePropertyName workingSetTrimEnabled -NotePropertyValue $true -Force
$optimizer | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $optimizerPath -Encoding UTF8

[pscustomobject]@{
    TestProfile = $testRoot
    PreviewOnly = $true
    AssignmentMode = "AdaptiveSharedPools"
}
