# Potato Launcher v1.0.113 — minimized clients stay near 60

- **Minimized clients no longer spike CPU and GPU.** A minimized FFXIV stops drawing frames, so no frame cap (display or NVIDIA driver) paces it any more and it spins at 240-400 loops per second. While a client is minimized, Potato now coarsens only its timer (CPU speed is never throttled), bringing it back to about 60. Restoring the window lifts this immediately.
- The Optimizer warns when any client runs far above your target FPS.
- Lighter monitoring: Potato reads fewer GPU counters and refreshes the Optimizer every 2 s.
