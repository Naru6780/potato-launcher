# Potato Launcher

**Version:** `1.0.76`

Windows launcher for Final Fantasy XIV / XIVLauncher account groups.

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
- Built-in help window for quick feature explanations.
- Slick rounded gradient buttons plus animated top-right image bandroll for featured Lodestone updates.
- Built-in Optimizer monitor with per-client CPU, GPU, memory, priority, and affinity metrics.
- Optional Optimizer controls for CPU lanes, client priority, and working-set trims.
- Sort accounts alphabetically, by last connected, or by the selected band.
- Manual Lodestone profile assignment can backfill XIVLauncher character metadata with a backup.
- Account metadata export/import with append, merge, replace, and overwrite modes.
- Band `band.json` save/import support plus browse-based band export with append, merge, replace, and overwrite import modes.
- Account order and last-connected state live in `%APPDATA%\Potato Launcher\accountList.json`, separate from `settings.json`.
- Account export/import carries custom account order and last-connected state.
- The Accounts panel can be resized with the splitter between Accounts and Band Manager.
- Roster drag-and-drop shows an insertion marker while moving accounts.
- Configurable launch cooldown for band queues.
- Theme folders with background images and per-theme music playlists.
- Built-in FFXIV news panel and emergency `Kill FFXIV` button.
- Settings, bands, account order, and portrait cache are stored in `%APPDATA%\Potato Launcher` so app updates preserve them.
- Optimizer settings are stored beside the launcher settings in `%APPDATA%\Potato Launcher\optimizer.json`.
- Built-in GitHub release updater.
- Resizable main window with adaptive account, band, overlay, and drawer layout.

## Install

Download `PotatoLauncherSetup.exe` from Releases and run it. The installer lets you choose where Potato Launcher is installed and can create Start Menu and Desktop shortcuts.

The portable zip is still available as `PotatoLauncher.zip`. Extract it anywhere and run `Potato Launcher.exe`; keep `Potato Launcher.exe` and `Potato Launcher Assets` together in the same folder.

## Project Notes

- Development context lives in `docs/goal.md`, `docs/research.md`, and `docs/release.md`.
- The latest downloadable builds are published as `PotatoLauncherSetup.exe` and `PotatoLauncher.zip` on GitHub Releases.
