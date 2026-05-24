# Changelog

## 1.0.28 - 2026-05-24

- Made the main Potato Launcher window resizable with a sensible minimum size.
- Added responsive layout behavior for account roster, band manager, status pill, settings drawer, loading overlay, launch picker, and news panel.

## 1.0.27 - 2026-05-24

- Replaced the oversized Windows icon list with a compact custom character roster grid.
- Added roster tile selection, double-click launch, themed selection styling, and strict refresh-needed tile states.
- Cached full-body Lodestone portraits alongside face portraits for future character card views.

## 1.0.26 - 2026-05-24

- Added Settings support for switching the account list between text and Lodestone portrait icons.
- Added automatic `Character@World` mapping when a launched client reaches the ready title.
- Added strict Lodestone icon refresh and local account portrait caching without fake fallback icons.

## 1.0.25 - 2026-05-23

- Moved the main-window music mute toggle beside the Kill FFXIV button so it no longer overlaps the mascot area.

## 1.0.24 - 2026-05-23

- Added a main-window music mute toggle for quick access.
- Kept the Settings mute checkbox synced with the main mute button.

## 1.0.23 - 2026-05-23

- Updated the loading status wording while waiting for each character title.

## 1.0.22 - 2026-05-23

- Added a configurable band launch cooldown with a default minimum of `0` seconds.
- Separated queue pacing from loading completion so clients can start on cooldown while the loading screen waits for every `Character@World` title.

## 1.0.21 - 2026-05-23

- Replaced character-selection screenshot detection with FFXIV window-title readiness.
- Advanced band queues after the new game window title stabilizes as `Character@World`.

## 1.0.20 - 2026-05-23

- Changed band queue readiness to wait for the FFXIV character-selection screen.
- Kept TCP connection checks as supporting status while the visual readiness gate waits for character selection.

## 1.0.19 - 2026-05-23

- Removed unused legacy GIF/loading splash helper classes.
- Kept the current launcher feature set intact while trimming dead internal code.

## 1.0.18 - 2026-05-23

- Removed arbitrary in-game wait and old launch cooldown countdown.
- Added client-aware queueing that tracks the newly launched `ffxiv` / `ffxiv_dx11` process.
- Advanced band launches only after the new game client has an established TCP connection.

## 1.0.17 - 2026-05-23

- Added an extra in-game wait setting after XIVLauncher handoff.
- Kept the loading screen and loading music alive during the extra wait.

## 1.0.16 - 2026-05-23

- Added `Stop music when all loaded`.
- Added music volume control.

## Earlier 1.0.x Highlights

- Added first-run launch method selection.
- Added separate Instanced and Shared band managers.
- Added theme folders, custom backgrounds, mascot/loading GIF assets, and per-theme music playlists.
- Added FFXIV news overlay.
- Added GitHub release updater with changelog popup.
