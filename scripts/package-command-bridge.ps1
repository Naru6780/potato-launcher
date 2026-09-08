param([string]$Configuration = 'Release', [Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (-not $destinationPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Bridge staging destination must be a subdirectory of the repository.'
}

# Pin the official API-15 reference distribution, not a developer's local Dalamud files.
$revision = '82a618838726b7964c17a553497c8535748c623c'
$expectedHash = '1DE51952991E2B5050E926776872FC9D72348C48A34C572EF74C9A14911B5099'
$cache = Join-Path ([IO.Path]::GetTempPath()) "PotatoBridgeRefs-$revision"
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'dalamud.zip'
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/goatcorp/dalamud-distrib/$revision/api15/latest.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Dalamud reference archive checksum mismatch.' }
$refs = Join-Path $cache 'refs'
Expand-Archive -LiteralPath $archive -DestinationPath $refs -Force
$project = Join-Path $repoRoot 'bridge\PotatoCommandBridge\PotatoCommandBridge.csproj'
dotnet build $project -c $Configuration "-p:DALAMUD_HOME=$refs" -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw 'Command bridge build failed.' }
$output = Join-Path $repoRoot "bridge\PotatoCommandBridge\bin\$Configuration"
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
# Runtime dependencies come from Dalamud. Never redistribute its DLLs or user profiles.
foreach ($name in @('PotatoCommandBridge.dll', 'PotatoCommandBridge.json', 'PotatoCommandBridge.deps.json')) {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $destinationPath $name) -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\command-studio.md') -Destination (Join-Path $destinationPath 'README.md') -Force
