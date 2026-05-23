# Potato Launcher

**Version:** `1.0.21`

Portable Windows launcher for Final Fantasy XIV / XIVLauncher account groups.

## Features

- First-run launch method picker: `Instanced` or `Shared`.
- Instanced mode for per-profile BAT launchers.
- Shared mode for the default XIVLauncher account list from `accountsList.json`.
- Separate autosaved band manager per launch method.
- Client-aware band queueing that waits for the new FFXIV window title to switch to `Character@World` before launching the next account.
- In-app loading screen with theme music, mute, volume, and stop-when-loaded controls.
- Theme folders with background images and per-theme music playlists.
- Built-in FFXIV news panel and emergency `Kill FFXIV` button.
- Portable settings stored beside the executable.
- Built-in GitHub release updater.

## Portable install

Download the latest `PotatoLauncher.zip` from Releases, extract it anywhere, and run `Potato Launcher.exe`.

Keep `Potato Launcher.exe` and `Potato Launcher Assets` together in the same folder.

## Project Notes

- Development context lives in `docs/goal.md`, `docs/research.md`, and `docs/release.md`.
- Codex handoff rules live in `AGENTS.md`.
- The latest downloadable build is published as `PotatoLauncher.zip` on GitHub Releases.
