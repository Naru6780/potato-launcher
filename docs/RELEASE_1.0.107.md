# Potato Launcher v1.0.107

## What's new

- **Rollback from Settings:** choose one of the three previous published stable versions. The launcher backs up its application files and profile JSON before replacing files. Current choices: v1.0.106, v1.0.105 and v1.0.104.
- **CPU-aware launching:** wait for three consecutive one-second readings below your configured CPU threshold before starting the next client. Measurement failures or a five-minute busy timeout stop the queue. This is pacing, not a CPU hard cap.
- **CPU optimizer:** hardware-aware shared pools with Windows-scheduling fallbacks, safer affinity restoration, and removal of Rescue CPU. On single-cache-domain CPUs, BalancedShared uses all cores rather than promising a special scheduling boost.
- **Per-client RAM trimming:** configurable MB trigger, sweep interval and cooldown; main and foreground clients are protected. The experimental combined band budget is not active.
- **Monitoring:** separate system commit warning, improved GPU 3D measurements and optional PresentMon FPS captures.
- **Launcher stability:** reduced minimized animation work, reused desktop-pet frame buffers, safe pet shutdown on drawing-resource failure, and optimizer footer flicker fixes.

## Important notes

- Settings remain in `%APPDATA%\Potato Launcher`; packages do not include user profiles. Rollback backups are stored locally under `Rollback Backups`. Older builds may interpret newer settings differently or lack current game compatibility/plugin-free features.
- Trimming resident RAM does not release committed allocations. The launcher does not change Windows pagefile settings. Adequate commit capacity is still required.
- A separately reported news-banner rendering out-of-memory failure under extreme memory pressure remains unresolved. The Artemis fix is not a guarantee against every out-of-memory failure.
- No guaranteed FPS uplift or 16-client capacity is claimed. Actual results depend on hardware, game settings and workload. CPU hard throttling and GPU scheduling changes are not included.
- 256 automated tests pass locally. Live full rollback/recovery and 16-client FPS performance are not fully validated by that test suite.
- Locally installed 1.0.107 previews share the same numeric version as this release; their updater may report up to date. Install the stable package manually to replace a preview.

Downloads: use `PotatoLauncherSetup.exe` for installation, or `PotatoLauncher.zip` for portable use.
