# Potato Launcher v1.0.111 — Optimizer you can steer by: live FPS, capacity, diagnostics

The Optimizer window now answers the questions that matter when stacking clients.

- **Live FPS for every client** (green at your target, red below). Read safely from the game's own frame counter: read-only, no injection, no admin, pinned to the supported game build.
- **"8/8 at 60 FPS"** at a glance, with system CPU, GPU and RAM.
- **Capacity estimate:** "Room for about N more clients: X runs out first", where X is CPU, RAM in use, or RAM + pagefile (committed memory). Based on what your running clients actually cost.
- **Diagnostics** with the fix: clients below target, Process Lasso ProBalance demoting clients, Dalamud plugin collections loaded into every client.
- **Target FPS** setting (default 60).
- Cleaner layout: roles are Playing / Main / Background; CPU lanes are an advanced option whose controls stay greyed out until enabled.
