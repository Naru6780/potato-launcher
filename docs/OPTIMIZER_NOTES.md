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
- Hard CPU affinity lanes on a single-CCD CPU: only removes cores from clients (every core is the same, and each
  client runs several threads that then queue on the same core); priority already protects the main. Removed in
  1.0.119. On a CPU with unequal cores (dual-CCD X3D, Intel P/E) the right tool would be a simple "keep clients
  off the slow cores" mask, not a per-client lane allocator.
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
