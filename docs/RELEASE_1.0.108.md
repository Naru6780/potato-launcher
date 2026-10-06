# Potato Launcher v1.0.108 — choose your DLSS 5 clients

## Added

- **DLSS 5 clients.** Every client shares one game folder, so a ReShade-based DLSS 5 install (for example RenoDX DLSS) used to load in all of them. Now you pick exactly which clients use it:
  - Settings → **DLSS 5 clients** opens a checklist of your accounts (with Select all / Clear), shows the detected game folder and whether ReShade and a DLSS add-on were found.
  - Right-click any account → **Use DLSS 5 on this client** toggles it directly.
  - Ticked clients start with their own ReShade profile (`%APPDATA%\Potato Launcher\DLSS5\ReShade`) that loads add-ons from the game folder. Every other client keeps ReShade but loads no add-ons.
  - The split is re-applied before every launch, so re-running a DLSS installer no longer turns DLSS on for every client.
- **Per-account launch environment variables** (advanced): `%APPDATA%\Potato Launcher\launchEnvironment.json`, keyed by `--account` value, applied only to that account's launch.

## Fixed

- An unreadable `settings.json` / `accountList.json` is no longer replaced by defaults without keeping a `.corrupt-<time>` copy; settings writes are flushed to disk first.
- Closing the launcher while Multiband is listening now restores running clients' CPU affinity.
- XIVLauncher's `accountsList.json` and `band.json` are written atomically; band save/export errors are shown instead of crashing.
- Broken loading GIFs, theme backgrounds or portraits no longer break painting; right-click menus no longer leak handles.
- A client that closes right after starting gives a clear error.
- Selecting a client in the Optimizer no longer re-saves it as the lowest-priority main.
- A Multiband listener that fails to start no longer reports "Listening".
- One vanished GPU counter no longer blanks all GPU readings; the desktop pet's fallback catches GDI+ errors.

## Notes

- Requires ReShade (`ReShade.ini`) and a ReShade add-on (`*.addon64`) in the game folder. Potato edits only the `[ADDON] AddonPath` line of the game's `ReShade.ini`.
- With no DLSS 5 clients selected and no `launchEnvironment.json`, launching behaves as in v1.0.107.
- Changes apply the next time a client is launched; running clients are not touched.
