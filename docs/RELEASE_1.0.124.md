# Potato Launcher v1.0.124 — exact 60 on your main

- **Main: exact 60 via NVIDIA cap** (Optimizer, on by default). The game's own 60 limit lands at ~56-58, more so on a busy main. Your configured main client now launches with Frame Rate None so the NVIDIA Max Frame Rate holds it at an exact 60. Every other client keeps the game's own 60, which is cheaper and the only cap that holds render-cut, covered or minimized windows.
- Potato reads the NVIDIA Max Frame Rate for FFXIV (read-only) and only does this when it equals your target, so the main can never launch uncapped. The Optimizer shows the NVIDIA cap next to the option and explains how to set it when it is off.
- Cost, measured on a 9800X3D main: about 0.8 point of CPU for that one client (7.07% at 60.0 FPS vs 6.26% at 57.9).
- Needs a main client set in the Optimizer (Client → Selected client role → Main) with its character known to Potato (Lodestone link). Takes effect when the main is next launched.
