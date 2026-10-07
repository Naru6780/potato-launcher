# Potato Launcher v1.0.118 — DLSS 5 clients back at 60

- Found the ~48 FPS on the DLSS 5 client: the RenoDX add-on retries its Streamline swap-chain discovery **every frame** (a DXGI factory plus 8 log lines per frame, ~500 KB/s of log). That fixed ~3.7 ms adds to the game's 60 fps limiter (16.7 + 3.7 ms = 48 FPS) but fits inside the frame under the NVIDIA driver cap.
- Potato now sets the in-game Frame Rate to **None** before launching a DLSS 5 client (the driver cap paces it at 60) and to the target option (60 fps) for every other client.
- DLSS 5 clients launch with ReShade logging off to stop the per-frame log spam.
- Optimizer: Cap column shows "driver" for such clients and only warns when a client is really running above target.
