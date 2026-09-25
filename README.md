# Potato Launcher

Potato Launcher is a Windows companion for FFXIV players who manage several XIVLauncher accounts. It keeps account groups, launch controls, Lodestone portraits, and client monitoring in one place.

[Website](https://naru6780.github.io/potato-launcher/) | [Latest release](https://github.com/Naru6780/potato-launcher/releases/latest)

## Highlights

- Save accounts into reusable bands and launch them in a predictable order.
- Automatically skip matching clients already running on this PC when launching a band.
- Wait for CPU usage to settle before each launch; optionally also wait for the client to initialize.
- Pair two trusted PCs on the same private network and start local and remote bands together.
- Browse accounts as a compact list or a portrait roster populated from the Lodestone.
- Watch FFXIV CPU, GPU, memory, affinity, and working-set status from the built-in monitor.
- Use Artemis as an optional desktop pet while the launcher is minimized.
- Install updates directly from GitHub Releases without losing local settings.

## Installation

Download `PotatoLauncherSetup.exe` from the [latest release](https://github.com/Naru6780/potato-launcher/releases/latest) and run it. A portable `PotatoLauncher.zip` is provided alongside the installer.

Potato Launcher stores its configuration in `%APPDATA%\Potato Launcher`, separately from the application files. Updating or reinstalling the launcher does not reset saved bands or account settings.

## First run

### Roll back an update

Settings → **Roll back version** lists the three newest published stable versions older than the running build, with a portable ZIP available. Choose a version and confirm to download it and restart the launcher. An active launch queue must finish or be canceled first. Game clients are not terminated.

The rollback installer backs up the current executable, assets and profile JSON files under `%APPDATA%\Potato Launcher\Rollback Backups` before replacing application files. It does not overwrite the active profile. Older builds may interpret settings differently or lack current game compatibility/plugin-free features. A failed replacement attempts to restore the previous application files; backups remain available for manual recovery. Internet access and write permission to the install folder are required. Unpublished local previews are not GitHub rollback choices. Use **Check for updates** in an older build to return to the latest published version.

1. Choose the launch mode that matches the XIVLauncher setup.
2. Select the folder containing the account batch files, or the shared `accountsList.json` folder.
3. Add accounts to a band and choose the launch timing in Settings.
4. Use **Launch band** to start the selected group.

The Artemis desktop pet can be enabled or disabled under Settings. Drag her with the left mouse button; while holding it, use the mouse wheel to resize her. The chosen size is remembered. This preference does not affect the short startup welcome animation.

## Two-PC Multiband

### Already-running clients

In the 1.0.105 preview, band launching reads character identity and home world directly from supported live DX11 clients before each member is launched. MoP's title updates are not required. A legacy `Character@World` title remains a discovery fallback when memory detection is unavailable, but never proves readiness. Without linked character metadata, discovery uses the account's display name. Clients launched during the current session are also tracked by process ID **and start time**.

Matching members appear as **Already running** and are skipped. Saved band checkboxes are not changed. Existing clients are not logged in again, initialized again, optimized, or subject to launch-helper cleanup. If every member is already running, no launch command is issued. Single-account launch remains an explicit launch action; this automatic skip applies to bands.

Discovery cannot assign an externally launched pre-login client to an account, or reliably identify a process Windows prevents Potato Launcher from inspecting. Unknown clients are not arbitrarily assigned. Keep character/home-world metadata accurate. Conflicting worlds or multiple same-name matches without a configured world stop the queue. Discovery is PC-local, not account-online detection across computers. Use one launcher instance per PC and avoid external launches during its queue.

### Plugin-free launch

Version 1.0.106 releases only FFXIV's two exact instance-limit mutex handles before launching another client, using the same mechanism as MoP from outside the game. It does not terminate game processes, inject code, or write game memory. Access or identity-validation failures stop the launch.

Potato maintains titles for clients it starts and confirms a loaded local player and territory through read-only game state, stable for three seconds. Character selection is still manual or handled by your chosen autologin plugin. In the 1.0.107 preview, CPU-aware pacing replaces the fixed cooldown: before each launch, wait for three consecutive one-second readings below the configured threshold (80% by default). A measurement failure or five-minute busy timeout stops the queue; Cancel remains available. This is pacing, not a CPU cap, memory reservation or proof of readiness. Unsupported game builds fail with a compatibility-update message instead of guessing; the current profile is pinned to game build `2026.09.15.0000.0000`.

Version 1.0.106 is available through the updater. The user confirmed an eight-client launch without MoP; see [verification notes](docs/PLUGIN_FREE_LAUNCH_VERIFICATION.md).

### Pairing PCs

Open **Multiband** on both PCs, pair them using the displayed code, then save a launch plan containing one band from each computer. The main PC coordinates the countdown and launch progress; credentials and local account files remain on their original PC.

Use this feature only on a trusted private network.

## Optimizing multiple clients (1.0.107 preview)

The goal is the largest **measured** client count sustaining approximately 60 FPS per client, including background clients. Sixteen is a representative workload, not a limit or a guarantee.

In Optimizer, **Balanced preset** enables shared CPU allocation and per-client threshold RAM trimming, preserving your **Trim trigger MB** setting. It does not change graphics, frame caps, drivers or your power plan. The policy detects Windows CPU topology; it does not assume a particular Intel/AMD model or adjacent SMT siblings. Symmetric cache domains can share the band evenly. Hybrid/unknown cores, asymmetric caches, small pools and lightly loaded machines retain all-core scheduling. On a single cache domain, this is equivalent to AllAvailableCores, not a dynamic FPS tuner. Multi-processor-group systems keep Windows scheduling; explicit affinity across groups is not implemented.

Automatic RAM trimming uses **Trim trigger MB** independently for each background follower. Auto trim at threshold acts when that client's resident RAM reaches the number; Pressure-aware additionally requires system RAM pressure. Sweeps default to ten seconds and each client has a thirty-second cooldown. Main and foreground clients are protected, and each sweep or manual action attempts only one client. The combined 30 GiB budget and rebound backoff have been removed. A threshold is not a hard cap, does not guarantee a specific post-trim size, and does not release committed allocations. Existing saved thresholds and enabled choices are preserved.

**Measure FPS** accepts the optional [PresentMon console executable](https://github.com/GameTechDev/PresentMon/releases) and captures 30 seconds without a game plugin. It reports each captured client's application present rate and p95 frame time, with raw CSV under the profile's `benchmarks` directory. It does not measure displayed-frame guarantees, automatically find the maximum client count, install PresentMon, or elevate itself. Missing captures and access failures are reported rather than treated as 0 FPS.

**Stop / restore** turns off both CPU automation and RAM trimming, then restores affinity changed during this launcher session. Process identity is checked and externally changed affinities are left alone. Process priorities are never reset. Rescue CPU has been removed entirely.

See the [performance testing and review notes](docs/OPTIMIZER_REVIEW.md) before comparing modes or changing graphics settings.

## Building from source

Requirements:

- Windows 10 or Windows 11, x64
- .NET 8 SDK

```powershell
dotnet restore
dotnet build PotatoLauncher.csproj --configuration Release
dotnet test tests/PotatoLauncher.Tests/PotatoLauncher.Tests.csproj --configuration Release
```

Release packaging scripts are kept in [`scripts`](scripts), and the Inno Setup definition is in [`installer`](installer).

## Project status

Potato Launcher is an independent community project. It is not affiliated with Square Enix, Final Fantasy XIV, XIVLauncher, or Dalamud.
