# Potato Launcher v1.0.121 — Test placements picks the right winner

- Fixed: Test placements counted a client as "at 60" only from 58.0 FPS. The game's own 60 fps limiter delivers ~58.0-58.2, so 0.1 FPS of noise decided the result and the most expensive placement could win. It now allows 3 FPS, like the rest of the Optimizer.
- The test now compares total CPU (what Task Manager shows) and ignores differences under 2 points, the run-to-run noise. On a 9800X3D with 8 clients, giving the main its own cores measured about 4 points lower than no pinning in two separate runs.
- Results from 1.0.120 are discarded, so Auto goes back to "Main gets its own cores" until you run the test again.
