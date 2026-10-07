# Potato Launcher v1.0.120 — CPU placement, measured on your PC

- **CPU placement (Auto, on by default).** Your main client (the configured main, or the FFXIV window you used last) gets cores of its own, on the 3D V-Cache CCD when the CPU has two (9950X3D / 7950X3D). Every other client stays inside one CCD. The main's share shrinks by itself when followers need the room, so 16 clients are never squeezed.
- Measured on a 9800X3D with 8 clients: total CPU 43.7% -> 40.0% at the same FPS.
- **Test placements** (Optimizer, about 3 minutes): tries no pinning, main-gets-its-own-cores and two-core lanes on your running clients and keeps the winner for this PC: most clients at the target, then best main FPS, then least CPU.
- New **CPUs** column, and two new warnings: another program (Process Lasso's ProBalance) changing client priority or cores, and a client that loads the DLSS 5 add-on while the in-game frame limit is on (it then holds only ~49).
