# Multi-client performance review — 2026-09-23

## Preview 4: user-requested threshold restoration

The following budget-policy descriptions are historical. The active trimmer now uses a per-client MB trigger again, with Threshold and PressureAware modes. Saved BandBudget mode migrates to Threshold without changing the saved per-client number or enabled flag. Sweeps use TrimIntervalSeconds and PID/start-time-keyed cooldowns use TrimCooldownSeconds; no global rebound backoff or game-build readiness dependency remains. Main and foreground clients remain protected. Manual trimming processes at most one background follower. No active setting is a forced cap or a remedy for exhausted commit. The news-banner OOM reported after preview 3 remains a separate outstanding issue, not fixed by this rollback.

## Preview 3 crash follow-up

The user's exception identifies an unhandled GDI+ OutOfMemoryException in ArtemisDesktopPetForm.RenderCurrentFrame. This proves a missing error boundary in optional artwork, not a game memory leak. The renderer now reuses its frame/pixel buffers and permanently suspends the pet for the session after an allocation or native drawing failure. It frees its artwork and stops its timer; direct form disposal also releases resources. Regression testing injects an allocation failure through the real form, verifies hiding/cleanup/no retry, and checks buffer reuse across 50 frames.

Read-only launch sampling caught a new FFXIV process consuming 57–75% of total CPU while aggregate CPU reached 100%. CPU-aware launch pacing now waits for three consecutive one-second readings below the configured threshold (default 80%), including the first/single launch. Language-neutral PDH avoids localized counter-name failures. Measurement errors and a five-minute busy timeout stop the queue; cancellation is rechecked immediately before Process.Start. This is not a hard CPU cap, nor a guarantee the next launch will not saturate CPU.

System committed memory was approximately 98–99 GiB against a 101.6 GiB limit while over 23 GiB physical RAM remained available. The optimizer now displays commit separately and warns at 95%; no launch blocking, OS/pagefile edits, forced working-set cap or additional live game launch was performed. Working-set trimming cannot resolve exhausted commit. The game crash cause is not established by the launcher's pet stack trace. Temporary launch CPU throttling remains unimplemented pending safe lifecycle/recovery testing; no performance gain or 16-client/60-FPS result is claimed.

## Preview 2 revision (supersedes preview 1 RAM defaults below)

At the user's request, Balanced preset now enables a 30 GiB total-game resident-memory target. The old per-client threshold UI is removed. Existing trim enabled/disabled choices are preserved on load, while legacy trim modes migrate to BandBudget. The budget applies to all local FFXIV clients, not just the selected band; summing working sets conservatively double-counts shared pages.

One eligible client is trimmed, then observed for ten seconds. At least 128 MB and half of the initial resident reduction must persist to classify the trial as useful. System available-memory changes are reported but not attributed exclusively to the trim, because another client may be launching simultaneously. Helpful trials wait two seconds before the next client and two minutes before repeating that client. Rebounds pause all trims for two minutes and the affected client for ten minutes. Failures also cool down rather than retrying continuously.

Main, foreground, clients younger than 30 seconds, and clients not verified in-world are skipped. Unknown/unsupported game-state detection is also skipped. This does not impose a hard cap, prevent launches, guarantee no stutter, or release committed allocations. Continuous FPS-based trim feedback is not implemented; use a live FPS capture and watch gameplay to validate. CPU planning-only also suppresses memory trimming. No 5–6 GiB per-client reservation or launch-admission guard is included.

BalancedShared uses all logical processors on this machine's single cache domain, as verified by live `0xFFFF` masks on eight clients. It is not a novel dynamic scheduler. Irrelevant saved CPU counts are hidden in shared modes to avoid implying they are active.

Memory-policy regression tests cover budget triggers, sequential observation, lasting savings, concurrent memory consumers, rebound cooldowns, process reuse, failed trims and legacy-mode migration. Prior preview-1 findings below remain historical context.

## Objective and limits

Maximize the number of simultaneously rendering FFXIV clients sustaining a target of 60 FPS each. All clients matter, not only the foreground character. Sixteen clients is the representative workload. A 60 FPS target corresponds to about 16.67 ms per frame; an average alone can hide stutter.

This is a local preview, not a proven performance uplift or an automatic capacity tuner. No live affinity, graphics, registry, driver or power-plan changes were made during verification. No FFXIV clients were available at the end of this review. The optional PresentMon integration has parser tests but still needs an end-to-end live capture. No new stable release has been published for this work.

## Changes and rationale

| Area | Finding | Change |
| --- | --- | --- |
| Default CPU allocation | Main-first reservations can concentrate most clients onto a few physical cores | Shared allocation, no exclusive main/system reservation in BalancedShared |
| Portability | Adjacent logical indices are not guaranteed SMT siblings; equal logical counts do not imply equal performance | Use Windows core/cache information and efficiency classes; unknown or heterogeneous layouts use the full available mask |
| Cache locality | Separate symmetric cache domains may benefit from separate shared pools | Balance clients by pool width; retain full scheduling for asymmetric cache sizes, small pools, partial/overlapping domains, or fewer than two clients per pool |
| Advanced pools | Repeated blocks filled one pool first | Assign by current clients per logical processor instead |
| One-core mode | Followers previously lost the sibling logical processor | Assign the complete physical-core mask |
| Rescue CPU | Automatic and manual temporary allocation expansion | Removed code and UI, including hung-window polling |
| Restore | Reset all game/launcher processes and priorities | Track original affinity per PID + start time; restore owned changes only; report failures |
| Stop / preview | Automatic tick could reapply a restored plan | Stop disables automation; entering preview restores owned affinities |
| RAM | Periodic working-set eviction can cause page faults without reducing committed allocation | Off for new profiles/preset; one eligible client per explicit or automatic sweep; accurately labeled |
| GPU monitor | Sum of independent 3D engines could falsely report saturation | Aggregate processes per adapter/engine, then take the busiest engine; asynchronous cached sampling |
| Launcher overhead | Repeated topology enumeration and minimized theme video | Cache topology, pause minimized video/decorative updates |
| UI | Late data binding could show the first CPU mode/count instead of the saved choice | Explicit item lists and guarded initialization; regression assertions after showing the form |
| Storage | Direct writes can truncate settings if interrupted | Write beside the destination and replace after successful serialization/write |

No GPU-core affinity is implemented. The GPU monitor covers 3D engine utilization, not VRAM allocation, video engines, or a complete bottleneck diagnosis. Core/cache topology is cached for the session; restart after changing CPU/VM topology. Processor-group-aware CPU placement is not implemented: multi-group PCs are left unchanged rather than pinned to a partial mask. Restoration after an abnormal launcher crash is not guaranteed because ownership is held in memory.

Existing settings are preserved. In particular an existing user's RAM trimming remains enabled until they disable it or choose Balanced preset. CPU roles still support advanced main-first modes and trim exclusions; they do not reserve cores in BalancedShared.

## Reviewed surrounding paths

- Band launch/readiness and duplicate-client discovery: retain the v106 handoff fix, PID/start-time tracking, ambiguous-client rejection and supported-build read-only readiness checks. No change to character selection or the exact instance-limit handle mechanism.
- Launch-helper cleanup: retain process-name and process-start identity checks; do not expand cleanup to game processes.
- Multiband: retain paired-device certificate/token handling and existing replacement settings writes. No automatic network or credential changes.
- Updater: currently downloads/extracts the archive before comparing versions. This can waste bandwidth and extraction can stall the UI; a metadata-first, integrity-checked updater is a separate remaining improvement, not silently rewritten in this performance pass.
- Account/settings persistence and decorative UI: replacement writes added for launcher-owned configuration; minimized theme video paused. User opt-in desktop pet behavior remains intact.

This is a focused review of these paths, not a claim that every feature or every hardware configuration has been exhaustively validated.

## Repeatable 60 FPS capacity test

1. Use one Potato optimizer instance. Record CPU/GPU/RAM, game resolution, graphics settings, frame cap, plugins and the exact scene. Keep drivers and power settings unchanged between runs.
2. Wait until all clients finish login/zoning and warm up. Ensure background/minimized game throttles do not deliberately limit clients below the target. Configure a suitable 60 FPS cap in game/driver settings yourself; Potato does not alter these settings.
3. Record the existing configuration first. Measure FPS selects an official PresentMon console executable, captures 30 seconds through ETW and retains CSV in the profile's `benchmarks` directory. Permission or version errors must be resolved before interpreting a result. Do not select unrelated executables.
4. Choose Balanced preset; repeat the same capture. Compare AllAvailableCores as well, especially on multiple cache domains. Prefer whichever improves the **worst client** without worsening frame-time spikes. CPU affinity alone is not necessarily an improvement over Windows scheduling.
5. Repeat captures at least three times. Inspect each client's average present rate, p95 frame time and capture duration. A short/missing capture is inconclusive, not a passing result. The CSV's primary swapchain is selected by sample count; application present rate is not displayed FPS.
6. Increase client count gradually, for example by two. The usable capacity is the largest count that keeps every client close to the target with acceptable tails and headroom in representative crowded scenes, not only an empty room. Never auto-launch unknown accounts to benchmark.
7. Stop / restore returns session-owned changes. Do not run other affinity managers simultaneously during a comparison.

### When CPU allocation is not enough

- CPU thread-limited: total CPU can look low while one render/main thread is saturated. Compare frame times with unrestricted scheduling before tighter pinning. Reducing character/effect/shadow workload may matter more than affinity.
- GPU-limited: compare lower per-client resolution/render scale and graphics cost while holding client count constant. Check VRAM through the vendor/OS monitor separately. GPU 3D utilization alone does not establish VRAM pressure.
- RAM pressure: working-set trimming is not a capacity upgrade. Look at committed memory and paging, not only smaller resident numbers after a trim. Reduce clients/settings or increase physical memory if paging causes stalls.
- The current preview does not automate graphics edits or choose a GPU for each process. These need separate profile-aware implementation and controlled measurements, not generic registry tweaks.

## Verification

- Baseline: 193 tests passed before the new regression tests.
- Expanded suite: 218 tests, covering allocation fallbacks, symmetric domains, hybrid efficiency classes, native-mask width, process reuse, restoration failures, GPU aggregation, FPS parsing, atomic writes and existing launcher functionality.
- UI rendering/layout tested at 1120×780 and 940×680, with no live optimizer enabled. Render artifacts are under `artifacts/optimizer-review-renders`.
- Release-configuration tests passed (218/218). A self-contained x64 preview ZIP was built and checked: 52 entries, executable/assets present, no persisted user settings included. Product version is `1.0.107-preview.1`. The ZIP is under `artifacts/optimizer-107-preview-release`.
- Live results remain unmeasured. Unit tests and successful packaging do not establish improved game FPS.

## Primary references

- [Windows processor efficiency classes](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-processor_relationship): heterogeneous core detection.
- [Windows affinity masks](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setprocessaffinitymask): processor-group restrictions.
- [EmptyWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset): eviction of resident pages.
- [GPU utilization accounting](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/): busiest-engine aggregation.
- [PresentMon console documentation](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md): timed capture and legacy present-interval metrics.
