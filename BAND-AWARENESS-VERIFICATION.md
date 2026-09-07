# Band client awareness — v1.0.97 verification

## Scope and root cause

The v1.0.96 band queue unconditionally called `StartAccountAndWaitForClientAsync` for every selected member. Existing character-title lookup was used for initialization and per-account termination, not for a pre-launch check. Saved band membership was therefore mistaken for the list of clients that still needed launching.

This change was built from the v1.0.96 commit `675e492` in a separate `codex/band-client-awareness` worktree. The older FF14 Optimization checkout and its unfinished optimizer changes were preserved. The checks below were completed locally before the user authorized publishing v1.0.97 through the normal release workflow. No running launcher or game client was replaced during verification.

## Changed files

- `RunningClientAwareness.cs`: read-only local FFXIV process discovery, exact character/world matching, process-start-time validation, and an injectable launch-or-skip operation.
- `Program.cs`: band queue integration, skipped-member reporting, overlapping-queue prevention, readiness-task cleanup, and help text. Both Shared and Instanced modes and the local side of Multiband use this band queue.
- `tests/PotatoLauncher.Tests/RunningClientAwarenessTests.cs`: 23 new test cases, including a read-only live discovery test.
- `PotatoLauncher.csproj`: local build version 1.0.97.
- `README.md`, `CHANGELOG.md`: behavior and limitations.

## Verification performed on 2026-09-07

- Baseline: 134 tests passed.
- Final: 157 tests passed, none failed or skipped.
- Release build: succeeded, zero warnings/errors.
- Release publish and portable ZIP packaging: succeeded; executable reports 1.0.97.0. ZIP verification found the executable and assets and no persisted user configuration files (52 entries).
- `git diff --check`: passed.
- New discovery/test file whitespace checks: passed. Checking the whole modified Program.cs reports 351 existing whitespace diagnostics; all flagged source lines already exist in the v1.0.96 baseline. No unrelated reformatting was performed.
- Live read-only dry run: discovered one actual FFXIV process with a Character@World title and returned Already running; the launch callback was not invoked. The same process remained running afterward.
- Automated behavior: 8/16-member queues launch only missing members in order; repeated all-running queues issue no launch callbacks; exact/case-insensitive names; character/world ambiguity; exited clients; reused PIDs; tracked generic/loading titles; inaccessible start time; cancellation; failed-launch retry; skipped-row display text.

## Remaining manual acceptance checks

These tests require actual launches or a second PC and were not performed automatically:

1. Close the old Potato Launcher, leaving an existing logged-in client running. Start the new executable from `publish` (keep the adjacent asset folder), or extract the entire portable ZIP and start it there.
2. Ensure that client's linked character/world matches its Character@World title. Select a band containing it and one offline account. Click Launch band. Confirm the online member shows Already running and only the offline member starts; note the existing process ID before/after to confirm it is unchanged.
3. Launch the same band again. Confirm zero new FFXIV/XIVLauncher processes and the final summary reports all members skipped. Verify band checkboxes are unchanged.
4. Close one member and launch the band again. Confirm only that member starts. Repeat in Shared and Instanced modes, with both initialization-wait settings and the normal cooldown.
5. Try a second launch while a queue is active. Confirm it is rejected without interrupting the first queue. Cancel during loading, allow cancellation to finish, then retry and confirm any now-identifiable running members are skipped.
6. Repeat with 8 and 16 actual clients. While earlier members initialize, manually start a later member and let its character title appear before its queue turn; verify it is skipped.
7. Put v1.0.97 on both paired PCs. With some band members already running on each, execute Multiband. Confirm each PC skips only its own existing members and the combined progress includes Already running.

## Reliability/security review and limits

- No game-memory offsets, plugin hooks, credentials, network discovery, telemetry, affinity changes, or renderer changes were introduced. Existing clients bypass launch/helper-cleanup code. No settings schema or band membership changes were introduced.
- Discovery is local and on demand; there is no new background polling loop. Process IDs are not trusted for tracking without a matching start time.
- Character-title discovery depends on accurate metadata and a character title supplied by the existing game/launcher/plugin setup. Generic/blank titles and inaccessible external clients cannot be assigned to an account reliably. Unknown clients therefore do not suppress unrelated launches.
- A matching name on a conflicting world stops the queue with an explanation rather than risking a duplicate; this can require checking a world visit or stale metadata.
- The launch queue guard is per launcher instance, not a cross-process/cross-PC account lock. Concurrent external launches and cancelled handoffs that have not yet produced an identifiable game process remain potential races. Use one Potato Launcher instance per PC and let a pending handoff settle before retrying. A future account/session handshake would be needed to cover unidentified pre-login clients reliably.
- Single-account launch remains an explicit launch action. Automatic skip applies to band launching.
- The updater reads the latest published release's `PotatoLauncher.zip`; a source push alone does not distribute an update. Release publication requires the versioned ZIP and installer from the release workflow.
