param(
    [string]$Configuration = "Release",
    [string]$PublishDir = "publish",
    [string]$ReleaseDir = "release"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishPath = Join-Path $repoRoot $PublishDir
$releasePath = Join-Path $repoRoot $ReleaseDir
$assetsSource = Join-Path $repoRoot "Potato Launcher Assets"
$assetsPublish = Join-Path $publishPath "Potato Launcher Assets"
$zipPath = Join-Path $releasePath "PotatoLauncher.zip"
$persistedFiles = @("settings.json", "accountList.json", "band.json")

function Assert-UnderRepo([string]$PathToCheck) {
    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $fullPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside repository: $fullPath"
    }
}

Assert-UnderRepo $publishPath
Assert-UnderRepo $releasePath
Assert-UnderRepo $assetsPublish

Set-Location $repoRoot
if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}
dotnet publish .\PotatoLauncher.csproj -c $Configuration -o $publishPath

foreach ($fileName in $persistedFiles) {
    Remove-Item -LiteralPath (Join-Path $publishPath $fileName) -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $assetsPublish -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $assetsSource -Destination $assetsPublish -Recurse -Force

if (Test-Path -LiteralPath $releasePath) {
    Remove-Item -LiteralPath $releasePath -Recurse -Force
}
New-Item -ItemType Directory -Path $releasePath | Out-Null
Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = $zip.Entries | Select-Object -ExpandProperty FullName
    $hasExe = $entries -contains "Potato Launcher.exe"
    $hasAssets = [bool]($entries | Where-Object { $_ -like "Potato Launcher Assets/*" -or $_ -like "Potato Launcher Assets\*" } | Select-Object -First 1)
    $persistedEntries = @($entries | Where-Object { $persistedFiles -contains (Split-Path $_ -Leaf) })
    $hasPersistedFiles = $persistedEntries.Count -gt 0

    if (-not $hasExe) { throw "Release zip is missing Potato Launcher.exe." }
    if (-not $hasAssets) { throw "Release zip is missing Potato Launcher Assets." }
    if ($hasPersistedFiles) { throw "Release zip contains persisted user data: $($persistedEntries -join ', ')" }

    [pscustomobject]@{
        Zip = $zipPath
        EntryCount = $entries.Count
        SizeBytes = (Get-Item -LiteralPath $zipPath).Length
        HasExe = $hasExe
        HasAssets = $hasAssets
        HasPersistedFiles = $hasPersistedFiles
    }
}
finally {
    $zip.Dispose()
}
