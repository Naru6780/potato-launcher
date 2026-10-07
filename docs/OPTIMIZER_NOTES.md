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
- Hard CPU affinity lanes on a single-CCD CPU: only removes cores from clients; priority already protects the main.
- An "FPS rescue" priority loop: zero-sum when the CPU is saturated.
- Constant working-set trimming at a fixed threshold: re-faults; pressure-aware trimming is fine (user keeps it on).

## Measurement
- FPS: `ExternalGameState.ReadFrame` (Framework* static via `48 8B 1D ?? ?? ?? ?? 8B 7C 24`, FrameCounter @0x16D0).
- GPU total: NVML (`NvidiaGpuUsage`); Windows 3D-engine counters under-report DLSS work.
