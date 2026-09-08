# Command Studio (experimental)

Command Studio is a launcher-owned tree of editable buttons. Groups open children; actions send slash commands to ONE selected FFXIV client on this PC. It does not delete, edit, or replace your in-game QoLBar. It does not change MoP, login settings, rendering, or the optimizer.

## Setup and first test

1. Update Potato Launcher. The ZIP/installer includes a **Command Bridge** directory beside the launcher. Keep its DLL, JSON and deps.json together in a stable local directory. If using a standalone EXE, extract that directory from the release ZIP separately.
2. On ONE non-performing test client, use `/xlsettings` → Experimental → Dev Plugin Locations to add `Command Bridge\PotatoCommandBridge.dll`. In `/xlplugins` → Dev Plugins, enable **Potato Command Bridge**. This is a developer plugin, not a custom plugin-repository installation.
3. Log in and enter `/potatobridge on` in that client. This enables same-Windows-user local command delivery in that client only. It defaults OFF; logout, character changes, OFF or plugin reload invalidate the session. Do not arm clients you do not intend to control.
4. In Potato Launcher, open **Command Studio** in the Band Manager heading, click **Refresh clients**, then explicitly select the intended character/PID. The launcher never guesses an origin or sends to every client automatically.
5. Create a local test action: `/echo Potato bridge test`, wait 1500 ms, then `/echo Second step`. Apply & save. Click the action. Confirm each line appears once, in order, only in the chosen client's chat, about 1.5 seconds apart. This tests dispatch, not a band broadcast.
6. Change the first wait to 10000 ms, run, then click **Stop sequence** during that wait. The second step must not appear. Repeat using `/potatobridge off` during the wait; the next step must be rejected. Refresh and reselect after rearming.
7. Only after those checks, test `/wave` and one real job/gearset action in a suitable idle context. Finally test one intended MoP/linkshell broadcast and confirm each intended client acts once. Leave other clients unarmed unless they need to be selectable origins themselves. Do not use a live performance as the first test.

The launcher acknowledgement means the command was submitted to the game, not that a gearset existed, an emote completed, or MoP succeeded. Check in-game errors and outcomes. Native dispatch has compiled against installed and pinned official API-15 Dalamud references, but compilation and a simulated receiver cannot verify a live game hook. On load errors, collect the Dalamud exception log; do not bypass compatibility checks.

## Editing your tree

- On first use, if the normal XIVLauncher profile has a uniquely named **Jobs (Multi)** bar, a copy seeds the studio. **Import QoLBar…** imports any chosen bar as a new group, including other XIVLauncher profiles. No existing studio groups are replaced.
- Left tree: select to edit; double-click a group to open it. Center tiles: click groups to navigate, actions to execute; right-click to select for editing. Back/Home navigate without sending anything.
- Add actions or empty groups, duplicate whole subtrees, delete, move up/down, or change the **Parent group** to reorganize recursively. Group names are entirely user-defined. Depth is bounded at 16 and profiles at 2000 nodes to keep malformed imports from exhausting resources.
- The editor changes labels, game icon IDs, custom artwork, background/text colors, parent, action/group behavior, and command steps. **Apply & save button** commits edits. Tile size and animation preference apply to the entire studio.
- In the step grid, type one slash command per row, including `/`. Each row has **Wait ms**: the wait AFTER that command is acknowledged and BEFORE the next step. A blank final grid row adds another step. Reorder or remove steps with the adjacent controls. The final row's wait is not used.
- Waits can be 0–600,000 milliseconds, up to 64 steps per action. Zero means no additional delay; IPC, framework scheduling and the game still take time. This is not a real-time sequencer or an exact reproduction of QoLBar's internal timing. Imported multiline commands initially use a 180-ms wait between lines; edit it as needed. Game/chat flood limits still apply.
- Stop cancels remaining steps and waits. Commands already sent cannot be undone. A broken pipe/timeout can occur after a command ran: delivery is then uncertain. There is no automatic retry, resume after reconnect, or resend of earlier steps. A reused PID or changed arming session is rejected.

## Import details and limitations

Actual command text, order, nested definitions and icon IDs come from the chosen QoLBar file. Negative custom icon IDs map to the sibling `QoLBar\icons\<positive-id>.png`. Local artwork is read on demand; newly chosen artwork is copied into the launcher's own artwork directory. Game icon textures are read through the companion when a client is available. Missing artwork is labelled by icon ID, not replaced with a guessed job symbol. Icons only serve as artwork; a button's command defines its behavior.

An imported category that also has a command becomes a group with a **Use [name]** action inside it. This preserves that command without executing it merely because you opened the group. Existing `/qolvisible` actions still affect the in-game bars if explicitly run; they are NOT launcher navigation and can be removed from your local copy.

QoLBar `//` macro directives, incremental/random modes, macro waits and non-command shortcut types are not implemented. Imported special modes carry a warning; unsupported commands cannot run until edited into explicit supported steps. Ordinary slash commands still depend on their normal in-game handlers: `/moprun`, `mopbr`, QoLBar commands, gearsets and macros are not made available by the bridge itself. Custom Lua/macro files are never bundled. The editor does not fix malformed quotes or missing macros in the source configuration.

The current editor supports the customization listed above, not arbitrary CSS, scripting of the studio itself, drag-and-drop reordering, hotkeys, QoLBar special-state engines, or per-button animation designers. Use parent selection and move buttons for organization. It is an editable proof of concept, not a complete QoLBar clone.

## Storage, trust and diagnostics

Profiles live in `%APPDATA%\PotatoLauncher\Command Studio\profile.json` (or the selected `--data-dir`), with an atomically replaced `profile.json.bak` previous version. Custom artwork is under its `artwork` subdirectory. Do not share your profile without reviewing commands, paths and private macro names. QoLBar and its custom artwork remain local and are excluded from releases. A corrupt/unknown profile is not overwritten automatically; recover the backup while the studio is closed.

The receiver opens no TCP port: its named pipe is restricted to the same Windows user. That is a user boundary, not protection from other software already running as your Windows account. While armed, trusted local software can request slash commands, including chat. Never accept unreviewed profiles as instructions. Run launcher and game as the same user. Messages, commands and image dimensions are bounded. Game dispatch runs on the framework thread using Dalamud's supplied FFXIVClientStructs API, not hardcoded memory offsets.

The receiver writes request ID and submitted line counts to Dalamud's normal logs; it does not log raw command text or entire configs. When reporting a failure, include launcher/plugin versions, selected PID, whether bridge was ON, the launcher status message, and the relevant Dalamud exception/request ID. Redact chat and private scripts.

## Developer verification

`dotnet test tests/PotatoLauncher.Tests/PotatoLauncher.Tests.csproj -c Release` covers import, persistent customization, group navigation/editor rendering, protocol validation, PID identity, a real named-pipe mock receiver, and deterministic sequence/cancellation behavior. Tests use synthetic commands and do not dispatch game commands. Optional `POTATO_STUDIO_TEST_PREVIEWS` writes fixture-only UI renders.

Build the companion with `.NET 10` and `dotnet build bridge/PotatoCommandBridge/PotatoCommandBridge.csproj -c Release`. Normal development uses installed Dalamud references. Release packaging pins the official `goatcorp/dalamud-distrib` API-15 archive to commit `82a618838726b7964c17a553497c8535748c623c` and verifies SHA-256 before building; Dalamud runtime DLLs are not redistributed. Launcher still targets .NET 8. Live checks above remain required before relying on it in performances.
