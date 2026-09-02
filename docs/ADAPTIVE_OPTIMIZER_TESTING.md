# Adaptive optimizer local testing

The isolated test profile uses Planning only mode. Planning mode computes and displays planned affinity masks but does not modify running FFXIV processes; normal profiles default to Live optimization.

## Prepare an isolated profile

From the source checkout:

```powershell
.\scripts\prepare-adaptive-test.ps1
```

This copies the current local configuration into `test-profile` and enables `AdaptiveSharedPools` plus Planning only mode. The production `%APPDATA%\Potato Launcher` files are not modified by the test build.

## Start the test build

```powershell
.\scripts\run-adaptive-test.ps1
```

Open Optimizer and confirm:

1. CPU operation mode says `Planning only — no CPU changes`.
2. CPU lanes is `AdaptiveSharedPools`.
3. The intended client is marked Main and has priority 1.
4. Other configured mains appear as `Follower (main candidate)`.
5. The Planned column never overlaps the active main or reserved system processors.

Allocation, rescue, and memory-pressure transitions are written to `test-profile\optimizer-decisions.log`.

Before a live affinity test, close the production Potato Launcher or disable its CPU optimizer. Select `Live optimization — apply CPU affinity` only in the test build, apply the allocation, and begin with two clients before testing larger bands.
