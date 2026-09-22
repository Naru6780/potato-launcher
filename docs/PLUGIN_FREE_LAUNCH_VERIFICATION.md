# Plugin-free launch candidate 1.0.105

Status: public prerelease; the third-client launch without MoP remains unverified.

## Scope

No character selection, login input, account authentication changes, game-memory writes,
injected DLLs or plugin APIs. XIVLauncher remains the account launcher. Users select a
character manually or retain their existing autologin plugin (including MoP).

The existing launch cooldown remains a delay between launches, not proof of in-world readiness.
The wait-for-initialization option now waits for external game-state confirmation.

## Readiness implementation

ExternalGameState pins the exact SHA-256 of the installed DX11 executable:
`5BBC501DD5C7F22FD61A11D08C25356041D878DB7CD83203ADAE393E4DFACC44`
(game version `2026.09.15.0000.0000`). No other build is accepted.

It resolves four unique RIP-relative signatures from the executable's .text section,
then uses ReadProcessMemory only. Cross-checks: PlayerState.IsLoaded, local-player
pointer, two independently read names/content IDs/entity IDs, player object kind,
home-world ID, ConnectedToZone, TerritoryLoadState=2, nonzero territory, and no
BetweenAreas/BetweenAreas51/LoggingOut/CreatingCharacter flags. Identity and territory
must remain consistent for at least three seconds. Unknown is not ready.

The layouts/signatures are documented by FFXIVClientStructs at commit
`9e94dfb36dae19a0ad02a39bbbe39ae235dd6c6f`:
https://github.com/aers/FFXIVClientStructs/tree/9e94dfb36dae19a0ad02a39bbbe39ae235dd6c6f

Public world IDs/names were retrieved from the World sheet through XIVAPI on 2026-09-20.
They are bundled; runtime readiness makes no network requests. Unknown world names
are not guessed and are not used to replace saved character metadata.

## Window labels and process identification

Only clients registered after a Potato launch are relabelled. A PID must retain its
original process start time. Account labels have no @ separator and never establish
character login. Real Character@World titles are generated from validated memory.
The title monitor remains active until Potato closes or the client exits.
Externally started logged-in clients can be discovered from validated memory; generic
pre-login clients with no known account association cannot be assigned arbitrarily.
Avoid simultaneous external launches during a Potato queue; multiple new clients
cause an explicit ambiguity error rather than assigning the first process.

## Client-count mutexes

The exact count locks are:
`6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00` and
`6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game01`.

Before a launch, inspect verified ffxiv_dx11 processes in the current Windows session.
Duplicate handles locally, query type before name (never query file/pipe names),
accept only Mutant objects with exact names in BaseNamedObjects/current session,
revalidate the process and duplicate handle, then close that source handle only.
Access failures stop the launch. No unrelated handles or processes are terminated.
There is an unavoidable remote-handle reuse race between validation and closure;
this needs integration validation, not merely a unit-test result.

References establishing the FFXIV lock names/mechanism:
https://github.com/zunetrix/MasterOfPuppets/blob/main/MasterOfPuppets/Game/MultiboxManager.cs
https://github.com/BardMusicPlayer/BardMusicPlayer/blob/master/BardMusicPlayer.Seer/GameMisc.cs

## Tests so far

- Thirteen focused policy/signature/readiness/label tests passed.
- Full Release test suite passed: 193 tests, zero failures.
- Read-only probe identified eight live clients as in-world, territory 132; the user
  confirmed all eight were logged in. No window-title inputs are used by that probe.
- Hermes's original process exited. Other seven stayed in-world. A new process later
  appeared in-world. This does NOT prove character-selection or loading-screen behaviour.
- Subsequently, the user confirmed Hermes was at character selection. The read-only
  probe reported that same new process (PID 36040) as NotInWorld, territory 0,
  "No logged-in local player", while the other seven remained InWorld, territory 132.
  This validates the character-selection negative case with the current configuration;
  loading transitions and a fresh client without MoP still require validation.
- After the user logged Hermes back in, PID 36040 returned to InWorld, territory 132,
  on two probes separated by more than three seconds. All seven other clients remained
  InWorld. This confirms the observed selection-to-world endpoints, not continuous
  loading-transition sampling or the launch queue's end-to-end readiness gate.
- Read-only mutex inspection succeeded on all eight processes and found zero exact
  count locks. Existing MoP may already have removed them. No handles were closed.

## Required manual release gates

1. Disable MoP multibox/title handling on a fresh test client (user-controlled).
2. Leave it at character selection: probe must report NotInWorld, never Initialized.
3. Enter a login queue if available: it must not complete readiness.
4. Enter world / teleport: sample transition flags and require Loading before InWorld.
5. Verify labels on a Potato-started client before login, after login and after logout.
6. With no plugin removing count locks, launch first, second and third distinct accounts
   through the preview. Confirm the third succeeds and the first two remain healthy.
7. Repeat the same band: tracked clients should be skipped. Restart Potato with clients
   logged in and verify memory-based discovery skips them again.
8. Check cancellation, wrong-character selection, stale metadata, access denial and
   unsupported executable behaviour. No failure may be silently treated as ready.

Read-only diagnostic (does not launch clients or close handles):
```powershell
$env:POTATO_READ_ONLY_PROBE='1'
dotnet test tests/PotatoLauncher.Tests/PotatoLauncher.Tests.csproj -c Release --filter FullyQualifiedName~ExternalGameStateTests --logger 'console;verbosity=detailed'
```

A memory-based readiness feature needs compatibility maintenance after game patches.
Do not advertise this as patch-proof. Do not promote the prerelease to the stable
update channel before the remaining manual release gates pass.
