# Potato Launcher

Potato Launcher is a Windows companion for FFXIV players who manage several XIVLauncher accounts. It keeps account groups, launch controls, Lodestone portraits, and client monitoring in one place.

[Website](https://naru6780.github.io/potato-launcher/) | [Latest release](https://github.com/Naru6780/potato-launcher/releases/latest)

## Highlights

- Save accounts into reusable bands and launch them in a predictable order.
- Automatically skip matching clients already running on this PC when launching a band.
- Add a cooldown between clients or wait for each client to initialize before continuing.
- Pair two trusted PCs on the same private network and start local and remote bands together.
- Browse accounts as a compact list or a portrait roster populated from the Lodestone.
- Watch FFXIV CPU, GPU, memory, affinity, and working-set status from the built-in monitor.
- Use Artemis as an optional desktop pet while the launcher is minimized.
- Install updates directly from GitHub Releases without losing local settings.

## Installation

Download `PotatoLauncherSetup.exe` from the [latest release](https://github.com/Naru6780/potato-launcher/releases/latest) and run it. A portable `PotatoLauncher.zip` is provided alongside the installer.

Potato Launcher stores its configuration in `%APPDATA%\Potato Launcher`, separately from the application files. Updating or reinstalling the launcher does not reset saved bands or account settings.

## First run

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

### Plugin-free launch preview

Version 1.0.105-preview.1 releases only FFXIV's two exact instance-limit mutex handles before launching another client, using the same mechanism as MoP from outside the game. It does not terminate game processes, inject code, or write game memory. Access or identity-validation failures stop the launch.

Potato maintains titles for clients it starts and confirms a loaded local player and territory through read-only game state, stable for three seconds. Character selection is still manual or handled by your chosen autologin plugin. The launch cooldown is separate from readiness. Unsupported game builds fail with a compatibility-update message instead of guessing; the current profile is pinned to game build `2026.09.15.0000.0000`.

This is a public prerelease, excluded from the stable updater. The no-MoP third-client launch and other live checks remain pending; see [verification notes](docs/PLUGIN_FREE_LAUNCH_VERIFICATION.md).

### Pairing PCs

Open **Multiband** on both PCs, pair them using the displayed code, then save a launch plan containing one band from each computer. The main PC coordinates the countdown and launch progress; credentials and local account files remain on their original PC.

Use this feature only on a trusted private network.

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
