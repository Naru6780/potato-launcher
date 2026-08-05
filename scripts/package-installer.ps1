param(
    [string]$Configuration = "Release",
    [string]$PublishDir = "publish",
    [string]$ReleaseDir = "release",
    [string]$DesktopPath = [Environment]::GetFolderPath("DesktopDirectory")
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishPath = Join-Path $repoRoot $PublishDir
$releasePath = Join-Path $repoRoot $ReleaseDir
$installerScript = Join-Path $repoRoot "installer\PotatoLauncher.iss"
$setupPath = Join-Path $releasePath "PotatoLauncherSetup.exe"
$persistedFiles = @("settings.json", "accountList.json", "optimizer.json", "band.json")

function Assert-UnderRepo([string]$PathToCheck) {
    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $fullPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside repository: $fullPath"
    }
}

function Find-InnoCompiler {
    $candidates = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }

    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6, then rerun this script."
}

Assert-UnderRepo $publishPath
Assert-UnderRepo $releasePath
Assert-UnderRepo $installerScript

$project = [xml](Get-Content -LiteralPath (Join-Path $repoRoot "PotatoLauncher.csproj") -Raw)
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read <Version> from PotatoLauncher.csproj."
}

& (Join-Path $PSScriptRoot "package-release.ps1") -Configuration $Configuration -PublishDir $PublishDir -ReleaseDir $ReleaseDir

foreach ($fileName in $persistedFiles) {
    if (Test-Path -LiteralPath (Join-Path $publishPath $fileName)) {
        throw "Publish folder contains persisted user data: $fileName"
    }
}

$iscc = Find-InnoCompiler
& $iscc `
    "/DAppVersion=$version" `
    "/DSourceDir=$publishPath" `
    "/DOutputDir=$releasePath" `
    $installerScript

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer was not created: $setupPath"
}

$desktopCopy = $null
if (-not [string]::IsNullOrWhiteSpace($DesktopPath)) {
    if (-not (Test-Path -LiteralPath $DesktopPath)) {
        New-Item -ItemType Directory -Path $DesktopPath | Out-Null
    }
    $desktopCopy = Join-Path $DesktopPath "PotatoLauncherSetup.exe"
    Copy-Item -LiteralPath $setupPath -Destination $desktopCopy -Force
}

[pscustomobject]@{
    Installer = $setupPath
    DesktopCopy = $desktopCopy
    Version = $version
    SizeBytes = (Get-Item -LiteralPath $setupPath).Length
}
