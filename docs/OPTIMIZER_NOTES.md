# Optimizer notes (measured facts for future work)

Measured on the maintainer's PC: Ryzen 7 9800X3D (8C/16T, single CCD, 96 MB L3), RTX 4080 SUPER 16 GB, 64 GB RAM,
Windows 11 build 26200, FFXIV build hash 5BBC501D… (see `ExternalGameState.SupportedSha256`), 8 clients, 60 FPS cap.

## Costs per client
- Visible background client at 60 FPS: ~3% of total CPU, ~0.3-2.5% GPU, ~0.9 GB VRAM, ~10 GB committed RAM
  (Dalamud: ~30 plugins from globally enabled plugin collections "Bands" + "Quest"; vanilla is ~2-3 GB).
- Foreground/main client: ~9% CPU, ~20% GPU without DLSS 5; ~63% GPU with RenoDX DLSS 5 neural rendering.
- RAM is the first limit for 16 clients (commit), then VRAM; CPU fits (~60% for 16 at current cost).

## Frame pacing
- Visible (even fully covered) clients are paced at 60 by the game/DWM.
- **Minimized clients stop presenting** and their loop spins at 230-400 iterations/s (7-9% CPU each).
  - The NVIDIA driver frame cap (profile Max Frame Rate = 60, already set) does not apply: no presents.
  - Windows power throttling (EcoQoS execution speed and/or IGNORE_TIMER_RESOLUTION) has **no effect** on the loop rate.
  - A **job object hard CPU cap** does work: 3% -> 87 loops/s, 4% -> 125. `MinimizedClientLimiter` steers it from
    the measured loop rate (damped, every 3 s); settles at ~57-71 within ~1 minute.
- Windows timer throttling of covered/minimized windows is opted out (`PreventWindowsThrottling`) for all clients.

## Things measured to NOT help (don't re-add)
- The 1.0.118 lane allocator with fixed defaults (main 6 threads, follower lanes of 4, 4 reserved): on 16 threads it
  left 6 threads for every follower, which starved them. Removed in 1.0.119; replaced in 1.0.120 (below).

## CPU placement (1.0.120), measured 2026-10-07 on the 9800X3D, 8 clients, in-game 60 limit
| Placement | System CPU | Clients' CPU | At 60 | Main (DLSS loader) |
|---|---|---|---|---|
| No pinning | 43.7% | 31.2% | 7/8 | 49 |
| Main gets its own cores (0-7 / followers 8-15) | 40.0% | 28.4% | 7/8 | 50 |
| Two-core lanes | 40.7% | 28.4% | 7/8 | 50 |
- Pinning lowers CPU even on one CCD (fewer cores woken per client), with no FPS cost while the followers have room.
- The main's reserved cores are sized from the followers' measured load with 1.5x headroom (`CpuPlacementPlanner`),
  so 16 clients shrink the reservation instead of starving followers.
- Dual-CCD X3D (9950X3D/7950X3D): the main goes on the V-Cache CCD; a follower never straddles CCDs.
- `Test placements` measures all three on the user's PC (interleaved, 2 rounds x 28 s) and Auto keeps the winner.
  `tests/.../LiveMeasurementHarness.cs` runs the same test from the command line (POTATO_HARNESS=placement).
- Process Lasso's ProBalance demotes the busiest client (the main): Potato now counts priority/affinity changes made by
  other programs and reports them.
- 9950X3D, 16 clients (user's friend, 1.0.121 test, no main reserved because of the test bug fixed in 1.0.122):
  no pinning 65.1% total CPU (clients 53.5%), 15/16 at target, lowest 54; main gets its own cores 53.9% (43.1%),
  16/16, lowest 57; two-core lanes 51.1% (41.1%), 16/16, lowest 57. Pinning matters far more on two CCDs.
  With lanes live, his main rose from 46 to 56 FPS while 6 followers still shared its V-Cache CCD; 1.0.122 adds
  "Main gets the cache CCD" to test giving the main that whole CCD.
- Render-cut followers (Master of Puppets) and NoNPC: followers use ~0.3% GPU and ~0.4 thread each on the 9800X3D.
  Plugin code hooked on the game's frame runs on the game's main thread, so per-thread start-address attribution
  counts it as "game": Dalamud's own threads (~4%) are not the whole plugin cost.
- A client carrying the DLSS 5 loader with the in-game 60 limit holds ~49 in every placement: it is the loader's
  per-frame cost, not CPU contention.
- An "FPS rescue" priority loop: zero-sum when the CPU is saturated.
- Constant working-set trimming at a fixed threshold: re-faults; pressure-aware trimming is fine (user keeps it on).

## Measurement
- FPS: `ExternalGameState.ReadFrame` (Framework* static via `48 8B 1D ?? ?? ?? ?? 8B 7C 24`, FrameCounter @0x16D0).
- GPU total: NVML (`NvidiaGpuUsage`); Windows 3D-engine counters under-report DLSS work.

## Frame caps: what holds where (measured 2026-10-07, v1.0.115/116)
- The NVIDIA driver cap (profile Max Frame Rate = 60) only paces frames that are actually presented. A window fully
  covered by another window ran ~110 loops/s (5-6% CPU); minimized 250-400 (8%). With stacked clients, most windows
  are covered most of the time, so the driver cap alone never carried the load.
- The game's own limiter (System Configuration → Display → Frame Rate; FFXIV.cfg `Fps`) sleeps inside the loop and
  holds in every state. Options on this build (config table min 0 / max 3): 0 none, 1 main display refresh rate,
  2 = 60 fps, 3 = 30 fps. Engine state readable at Device (`48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 80 7B 08 00`,
  pointer): IsFrameRateLimited @0xA8, FrameRateLimit @0xAE. `FrameLimitEnforcer` writes `Fps` before each launch.
- With `Fps 2`: visible or covered client = 58-60 loops/s at ~2.5% CPU; minimized = ~49 (an extra ~3.5 ms per frame
  appears only while minimized; not timer throttling: power-throttling state was 0 on every client). A covered window
  is therefore the better "background" state than a minimized one.
- The client that still loads ReShade + RenoDX (the DLSS 5 client) holds only ~48 with the 60 limiter and costs ~6%
  CPU even with DLSS switched off in-game; the loader itself is the cost. Untick it in DLSS 5 clients when DLSS is not
  in use.
- Windows' TextInputHost repeatedly got stuck at ~5% CPU (equal to two clients); ending it brings it to 0% and Windows
  recreates it idle. `InputHostWatchdog` does this automatically.
- Per-client steady state now: ~2.5% CPU, ~0.3-2.5% GPU, ~0.9 GB VRAM, ~7 GB committed (plugins). CPU for 16 clients
  ≈ 40%; the limits are RAM commit and VRAM.

## In-game cap vs NVIDIA cap on the main (2026-10-07, 9800X3D, Artemis, 175 samples of 2 s)
- In-game 60: 57.9 FPS, 6.26% CPU (sd 0.64), 17.3 ms CPU per frame.
- Frame Rate None + NVIDIA 60: 60.0 FPS, 7.07% CPU (sd 0.66), 18.9 ms CPU per frame.
- The in-game limit is cheaper per frame too; the NVIDIA cap only buys an exact 60. Followers must use the in-game
  limit regardless (render-cut/covered windows present nothing for the driver to pace).
