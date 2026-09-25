# Potato Launcher v1.0.107 — v106-based replacement

This replaces the previous v107 packages at the maintainer's request. It is built from v106, not from the earlier v107 optimizer redesign.

## Preserved from v106

- Existing interface and optimizer controls/policies, including Rescue CPU.
- Configurable per-client RAM trimming and the fixed launch cooldown.
- Existing plugin-free client awareness and band handoff fixes.

## Added and improved

- Settings → **Roll back version**: choose **v1.0.106, v1.0.105 or v1.0.104**. Requires confirmation and an idle launch queue; backs up application files and profile JSON before replacing only executable/assets. Game clients stay running.
- Fewer idle news-banner redraws and no minimized bubble-animation redraws.
- Reused desktop-pet frame buffers; drawing-resource failures suspend and release optional pet/news artwork instead of escaping from those rendering paths.
- Safer launcher-owned settings writes, cleanup of unused process/counter objects, and less frequent failed GPU-counter discovery.
- Settings compatibility for the superseded v107 optimizer modes without discarding other saved preferences.

## Installation and limits

- **Already on the earlier v107? Reinstall manually using the downloads below.** The version remains 1.0.107, so its updater cannot detect this replacement. The previous source remains in Git history and is archived under `v1.0.107-before-v106-rebuild`.
- Use `PotatoLauncherSetup.exe` or the portable `PotatoLauncher.zip`. Packages contain no user profiles.
- No game process is closed for rollback. Older builds can lose fixes or compatibility. Backups remain under `%APPDATA%\Potato Launcher\Rollback Backups`; current settings are not overwritten by the rollback installer.
- 209 automated tests pass locally, including rollback selection/archive validation, rendering failure recovery and compatibility tests. No live FPS uplift or universal 16-client capacity is claimed. Working-set trimming still does not release committed memory; this build does not alter Windows pagefile settings.
