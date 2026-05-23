# Potato Launcher Release Workflow

Use this when a change needs to reach users through the in-app updater.

## Version

Update `PotatoLauncher.csproj`:

```xml
<Version>x.y.z</Version>
<AssemblyVersion>x.y.z.0</AssemblyVersion>
<FileVersion>x.y.z.0</FileVersion>
<InformationalVersion>x.y.z</InformationalVersion>
```

## Build

```powershell
dotnet publish .\PotatoLauncher.csproj -c Release -o .\publish
```

## Package

Always refresh assets manually before zipping:

```powershell
Remove-Item -LiteralPath 'publish\Potato Launcher Assets' -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath 'Potato Launcher Assets' -Destination 'publish\Potato Launcher Assets' -Recurse -Force
if (Test-Path .\release) { Remove-Item .\release -Recurse -Force }
New-Item -ItemType Directory -Path .\release | Out-Null
Compress-Archive -Path .\publish\* -DestinationPath .\release\PotatoLauncher.zip -Force
```

Verify the zip contains:

```text
Potato Launcher.exe
Potato Launcher Assets\
```

It should not contain `settings.json`.

## Publish

```powershell
git add .
git commit -m "Short release description"
git push

gh release create vx.y.z .\release\PotatoLauncher.zip `
  --repo Naru6780/potato-launcher `
  --target master `
  --title "Potato Launcher vx.y.z" `
  --notes-file .\release\vx.y.z-notes.txt
```

## Verify

```powershell
gh release view vx.y.z --repo Naru6780/potato-launcher --json tagName,url,assets
git status --short
```
