# Potato Launcher

**Version:** `1.0.38`

Portable Windows launcher for Final Fantasy XIV / XIVLauncher account groups.

## Features

- First-run launch method picker: `Instanced` or `Shared`.
- Instanced mode for per-profile BAT launchers.
- Shared mode for the default XIVLauncher account list from `accountsList.json`.
- Separate autosaved band manager per launch method.
- Right-click band naming so the manager stays compact.
- Client-aware band queueing that waits for the new FFXIV window title to switch to `Character@World` before launching the next account.
- Band Manager loading screen with theme music, mute, volume, and stop-when-loaded controls.
- Main-window music mute toggle.
- Text or compact Lodestone portrait roster account display.
- Startup Lodestone portrait refresh for mapped accounts.
- Right-click account roster options for setting/opening Lodestone profiles, sorting, and deleting accounts.
- Drag account ordering that also drives Band Manager display and launch order.
- Sort accounts alphabetically, by last connected, or by the selected band.
- Manual Lodestone profile assignment can backfill XIVLauncher character metadata with a backup.
- Account metadata export/import with append, merge, replace, and overwrite modes.
- Band `band.json` save/import support plus browse-based band export with append, merge, replace, and overwrite import modes.
- Account order and last-connected state live in portable `accountList.json`, separate from `settings.json`.
- Account export/import carries custom account order and last-connected state.
- The Accounts panel can be resized with the splitter between Accounts and Band Manager.
- Roster drag-and-drop shows an insertion marker while moving accounts.
- Configurable launch cooldown for band queues.
- Theme folders with background images and per-theme music playlists.
- Built-in FFXIV news panel and emergency `Kill FFXIV` button.
- Portable settings stored beside the executable.
- Built-in GitHub release updater.
- Resizable main window with adaptive account, band, overlay, and drawer layout.

## Portable install

Download the latest `PotatoLauncher.zip` from Releases, extract it anywhere, and run `Potato Launcher.exe`.

Keep `Potato Launcher.exe` and `Potato Launcher Assets` together in the same folder.

## Project Notes

- Development context lives in `docs/goal.md`, `docs/research.md`, and `docs/release.md`.
- Codex handoff rules live in `AGENTS.md`.
- The latest downloadable build is published as `PotatoLauncher.zip` on GitHub Releases.
