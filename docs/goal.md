# Potato Launcher Goal

**Current version:** `1.0.20`

Potato Launcher is a cute, portable Final Fantasy XIV launcher helper for people who run many XIVLauncher accounts.

The user-facing goal is simple: choose accounts or a named band, click launch, and let Potato Launcher sequence clients without forcing the user to manually babysit every XIVLauncher window.

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
- Launching a band should queue accounts safely and visibly through the in-app loading screen.

## Loading Behavior

The loading screen must stay inside the main app window and include a Cancel button.

The app currently waits for the newly launched `ffxiv` / `ffxiv_dx11` window to reach character selection before it advances to the next account. This is a non-invasive visual readiness signal, not a game-memory or plugin signal.

Future readiness improvements should prefer real client state over arbitrary cooldowns.

## Visual and Audio Experience

The app should feel like a playful FFXIV-themed launcher:

- animated moogle mascot
- theme backgrounds
- cute loading GIFs
- optional per-theme music playlists
- random custom theme at launch when enabled

Default color themes are simple built-ins and are excluded from random theme selection.

## Guardrails

- Do not inject into FFXIV or read game memory without an explicit user decision.
- Do not store passwords or manipulate XIVLauncher password data.
- Keep settings and folders portable.
- Keep the UI simple; avoid exposing XIVLauncher settings that XIVLauncher already handles.
