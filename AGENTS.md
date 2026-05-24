# Potato Launcher Agent Notes

## Project Goal

Potato Launcher is a portable Windows WinForms app for launching Final Fantasy XIV accounts through XIVLauncher.

The app supports two launch methods:

- `Instanced`: user-selected BAT files, usually one BAT per custom Dalamud/XIVLauncher profile.
- `Shared`: the default XIVLauncher account list from the user's selected XIVLauncher profile folder.

Keep the app friendly, portable, and simple for non-technical users sharing the extracted folder with friends.

## Development Rules

- Keep `master` as the working branch unless the user explicitly asks for another branch.
- Commit and push tested iterations to GitHub when the user asks for changes.
- Create a GitHub release only when the user needs the in-app updater to deliver a new app build.
- Do not add fallback launch behavior. If a launch path cannot be understood, report the issue clearly.
- Do not store or log XIVLauncher passwords.
- Do not delete or modify user XIVLauncher account data except when the user explicitly asks.
- Keep `settings.json` generated beside the executable; never include it in release zips.
- Keep `Potato Launcher.exe` and `Potato Launcher Assets` together in release packages.
- After publishing, manually replace `publish\Potato Launcher Assets` from the source assets before zipping.

## Build and Release

Use PowerShell from the repository root:

```powershell
.\scripts\package-release.ps1
```

Then create a GitHub release with asset name `PotatoLauncher.zip`. The package must include `Potato Launcher.exe` and `Potato Launcher Assets`, and must not include generated portable data such as `settings.json`, `accountList.json`, or `band.json`.

## Important Files

- `Program.cs`: the full app implementation.
- `PotatoLauncher.csproj`: version metadata and publish settings.
- `Potato Launcher Assets\Assets`: loading/mascot GIF assets.
- `Potato Launcher Assets\themes`: custom theme folders, backgrounds, and music playlists.
- `docs/goal.md`: player-facing product goal.
- `docs/research.md`: implementation decisions and known caveats.
- `docs/release.md`: release/update workflow.
