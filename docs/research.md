# Potato Launcher Research

**Current version:** `1.0.36`

This document keeps implementation decisions that matter for future Codex sessions.

## Launch Methods

### Instanced

Instanced mode reads `.bat` files from the selected folder. Each BAT usually targets a specific XIVLauncher install/profile folder.

The app parses supported `start` command BATs into a hidden `ProcessStartInfo` launch instead of opening a visible console window.

Instanced account order follows BAT filename numeric prefixes.

### Shared

Shared mode reads accounts from the user-selected XIVLauncher profile folder containing `accountsList.json`.

Shared mode launches the normal Local XIVLauncher executable from:

```text
%LOCALAPPDATA%\XIVLauncher\current\XIVLauncher.exe
```

Shared account order follows the natural order in `accountsList.json`.

## Account Flags

XIVLauncher account keys include flags such as:

```text
username-UseOtp-UseSteamServiceAccount
```

If `UseOtp` is true, Potato Launcher disables autologin for that account so XIVLauncher can ask the user for OTP.

Steam service account state is carried through the account key and should remain managed by XIVLauncher.

## Readiness Detection

`v1.0.22` separates band queue pacing from readiness:

- each account is launched after the previous launch handoff plus the configured cooldown
- default cooldown is `0` seconds, the minimum allowed value
- the loading screen stays visible until every launched client has a stable `Character@World` window title

Known caveat: title detection depends on the game client updating its Windows title. This is more precise than generic TCP connection detection for the launcher queue and avoids screenshot/OCR fragility.

Possible future improvement: add resource-stability detection for the specific new process by watching working set/private memory/GPU counters settle after the first heavy load spike.

Exact readiness would require a tiny Dalamud-side signal, such as a local marker or IPC message when the player enters world. Do not add that unless the user explicitly agrees to a plugin dependency.

## Assets

Release zips must include:

```text
Potato Launcher Assets\Assets
Potato Launcher Assets\themes
```

Theme folders may contain:

- one or more background images/videos
- one or more music files

Supported music playlist extensions currently include common audio formats handled by WPF MediaPlayer.

Account portraits are cached in:

```text
Potato Launcher Assets\Account Icons
```

The cache is keyed by the launcher account key or BAT identity. Portraits are refreshed only from saved Lodestone profile IDs or URLs. The app caches both the face portrait (`fc0`) for roster tiles and the full-body portrait (`fl0`) for richer character-card UI. The app does not use placeholder icons or guessed profile matches in roster mode; missing icons stay visibly unmapped until the user assigns a profile URL or imports account metadata.

On startup, mapped accounts with saved Lodestone profile URLs are refreshed so changed Lodestone portraits replace the local cache. Accounts without a profile mapping are skipped rather than guessed.

Manual Lodestone profile assignment in Shared mode can update XIVLauncher's `accountsList.json` for the matching `UserName`, `UseSteamServiceAccount`, and `UseOtp` entry. Potato Launcher only writes `ChosenCharacterName`, `ChosenCharacterWorld`, and `ThumbnailUrl`, and first creates a timestamped backup under `backups\PotatoLauncher`.

Settings account export writes a Potato Launcher transfer JSON with safe XIVLauncher account metadata plus portable Lodestone profile links from `settings.json`. Importing asks for `AppendAll`, `AppendNew`, `Merge`, `ReplaceExisting`, or `OverwriteAll`. Account matching uses `UserName`, `UseSteamServiceAccount`, and `UseOtp`; imported entries are written with password saving disabled and `accountsList.json` is backed up before import writes.

The Band Manager Save button and Settings export action write the current launch method's bands to `band.json` beside `settings.json`. Band import uses the same import mode chooser as account import, including duplicate-copy append and full overwrite when explicitly selected.

Band names are edited by right-clicking a band and choosing `Set name`; the old permanent rename text box was removed to keep the member list larger and the UI less cluttered.

Account ordering is stored in portable `accountList.json` as `SharedAccountOrder` and `InstancedAccountOrder`, separate from `settings.json`. Dragging in the account list updates that order. Band Manager does not have a separate reorder model; it mirrors account order and saves checked band members in that order. `LastConnectedUtc` stores the timestamp when Potato Launcher observes a successful `Character@World` title, enabling sort-by-last-connected.

## News and Updates

The in-app news panel uses XIVLauncher/Lodestone-style public endpoints:

- `https://frontier.ffxiv.com/v2/topics/en-us/banner.json`
- `https://frontier.ffxiv.com/news/headline.json`

The updater checks GitHub Releases for `Naru6780/potato-launcher`, downloads `PotatoLauncher.zip`, extracts it, copies over the current app folder, then relaunches.

The release zip must not contain `settings.json`.
