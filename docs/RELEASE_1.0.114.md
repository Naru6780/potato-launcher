# Potato Launcher v1.0.114 — minimized clients held near 60 (for real)

- A minimized FFXIV stops drawing frames, so no frame cap applies and it spins at 230-400 loops per second, pushing CPU to 100%. v1.0.113 tried Windows timer throttling, which turned out to have no effect.
- Now, only while a client is minimized, Potato puts a hard CPU-time cap on it and adjusts it from the client's measured rate to hold it near your target (60). Restoring the window removes the cap immediately. Tested live with 7 minimized clients: 250-400 down to about 57-71.
