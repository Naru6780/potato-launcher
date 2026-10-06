# Potato Launcher v1.0.108 — per-account launch environment

## Added

- Optional per-account environment variables, read from `%APPDATA%\Potato Launcher\launchEnvironment.json` and keyed by the account's `--account` value:

  ```json
  { "myaccount-False-False": { "RESHADE_BASE_PATH_OVERRIDE": "C:\\Games\\ReShade-DLSS5\\Main" } }
  ```

  The variables are applied only when that account is launched and are inherited by XIVLauncher, the Dalamud injector and the game client. Typical use: let one client load ReShade from its own base path (own `ReShade.ini`, add-ons such as DLSS 5) while every other client shares the game-folder setup.

## Notes

- Without the file, launching behaves exactly as in v1.0.107.
- Invalid JSON or unknown accounts are ignored; nothing else is affected.
- Built from v1.0.107. 211 automated tests pass locally, including new launch-environment tests.
