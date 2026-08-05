# Potato Launcher

Potato Launcher is a Windows companion for FFXIV players who manage several XIVLauncher accounts. It keeps account groups, launch controls, Lodestone portraits, and client monitoring in one place.

[Website](https://naru6780.github.io/potato-launcher/) | [Latest release](https://github.com/Naru6780/potato-launcher/releases/latest)

## Highlights

- Save accounts into reusable bands and launch them in a predictable order.
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

The Artemis desktop pet can be enabled or disabled under Settings. This preference does not affect the short startup welcome animation.

## Two-PC Multiband

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
