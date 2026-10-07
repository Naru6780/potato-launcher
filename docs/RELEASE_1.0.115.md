# Potato Launcher v1.0.115 — the cap that holds everywhere

- **Why clients ran hot:** the NVIDIA driver frame cap only paces frames that are actually shown. A client whose window is covered by another window ran ~110 loops/s (double the CPU) and a minimized one 250-400. With many stacked clients, most are covered most of the time.
- **The fix:** the game's own Frame Rate limit (System Configuration → Display Settings) holds in every state. Potato now sets it to 60 fps in FFXIV.cfg before every launch; clients already running pick it up when relaunched (or set it in-game once). Toggle in the Optimizer next to Target FPS.
- **Optimizer:** new **Cap** column shows each client's in-game limit ("game 60" or "none", orange) and a diagnostic names the clients that still have none, with the fix.
- Measured on 8 clients: covered clients went from ~110 FPS at 5-6% CPU to 59 FPS at ~2.5% each.
