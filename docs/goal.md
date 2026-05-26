# Potato Launcher Goal

**Current version:** `1.0.56`

Potato Launcher is a cute, portable Final Fantasy XIV launcher helper for people who run many XIVLauncher accounts.

The user-facing goal is simple: choose accounts or a named band, click launch, and let Potato Launcher sequence clients without forcing the user to manually babysit every XIVLauncher window.

The main window should be resizable so users with large rosters can make the account and band panels taller or wider. The Accounts panel should also be horizontally resizable with the mouse while Band Manager automatically uses the remaining space without repaint artifacts.

## Player Experience

On first run, the app asks which launch method to use:

- `Instanced`: launch accounts from a folder of user-created BAT files.
- `Shared`: launch accounts from XIVLauncher's shared `accountsList.json`.

The chosen method is saved in portable `settings.json` beside the executable. Users can change the method later in Settings.

## Band Manager

Bands are named account groups.

- Instanced bands and Shared bands are separate.
- Bands are created by the user; no default bands should be generated.
- Band edits autosave.
- Band names are changed from the band list right-click menu, not from a permanent text box.
- Launching a band should queue accounts safely and visibly through the Band Manager loading screen while leaving the account roster visible.
- Bands can be saved/exported as `band.json` beside `settings.json` and imported on another portable copy with an explicit import mode.

## Loading Behavior

The loading screen must stay inside the Band Manager area and include a Cancel button.

Band queues can pace client starts with a user-configurable cooldown. The loading screen remains visible until every launched `ffxiv` / `ffxiv_dx11` window title switches from `FINAL FANTASY XIV` to `Character@World`. This is a non-invasive window-title readiness signal, not a game-memory or plugin signal.

Future readiness improvements should prefer real client state over arbitrary cooldowns.

## Visual and Audio Experience

The app should feel like a playful FFXIV-themed launcher:

- animated moogle mascot
- theme backgrounds
- cute loading GIFs
- optional per-theme music playlists
- random custom theme at launch when enabled

Default color themes are simple built-ins and are excluded from random theme selection.

## Account Icons

Users can switch the account list between text and compact roster display in Settings. Roster mode uses real Lodestone character portraits only: Potato Launcher stores profile mappings from manual profile URLs or imports, then refreshes the matching Lodestone face and full-body portraits into the portable account icon cache. Missing mappings are reported instead of using fake icons or guessed profile matches.

Users can right-click an account to set or open the Lodestone profile URL. Manual profile URLs are treated as authoritative and are fetched directly from the character profile page.

Mapped Lodestone portraits refresh on every app launch so changed character profile images are picked up without manual refresh.

When the user manually assigns a Lodestone profile to a Shared-mode account, Potato Launcher may backfill XIVLauncher's `ChosenCharacterName`, `ChosenCharacterWorld`, and `ThumbnailUrl` fields for that account after creating a backup of `accountsList.json`.

Users can export Shared-mode account metadata, custom account order, last-connected state, and Lodestone profile links, then import them on another machine. Importing must ask for an import mode so the user chooses whether to append, merge, replace matching entries, or overwrite everything.

Users can drag accounts in the account list to define the shared display order. Band Manager mirrors that account order, and band launch order follows the checked member order shown there. Users can also sort accounts alphabetically, by the selected band, or by the most recent successful `Character@World` connection recorded by Potato Launcher. Account order and last-connected data live in portable `accountList.json`, not in `settings.json`.

Roster dragging should show a clear insertion marker before the user releases the mouse.

## Guardrails

- Do not inject into FFXIV or read game memory without an explicit user decision.
- Do not store passwords or manipulate XIVLauncher password data.
- Keep settings and folders portable.
- Keep the UI simple; avoid exposing XIVLauncher settings that XIVLauncher already handles.
