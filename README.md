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

Band launching checks live `ffxiv`/`ffxiv_dx11` processes before each member is launched. It matches the account's linked/remembered character name and world to the game's `Character@World` window title. Without linked character metadata, it uses the account's display name. A client launched during the current Potato Launcher session can also be recognized by its process ID **and start time**, even while its title is generic.

Matching members appear as **Already running** and are skipped. Saved band checkboxes are not changed. Existing clients are not logged in again, initialized again, optimized, or subject to launch-helper cleanup. If every member is already running, no launch command is issued. Single-account launch remains an explicit launch action; this automatic skip applies to bands.

Discovery cannot identify an externally launched client that has no character title (including login screens or setups without character-title updates), or a process Windows prevents Potato Launcher from inspecting. Those unknown clients are not arbitrarily assigned to accounts. Use accurate character metadata/titles before launching a band. Conflicting worlds or multiple same-name matches without a configured world stop the queue with an explanation; verify the account and any world visit first. Discovery is PC-local, not account-online detection across computers. Separate simultaneously running Potato Launcher instances or external launches can still race; use one launcher instance per PC and avoid starting the same account elsewhere during its queue.

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
