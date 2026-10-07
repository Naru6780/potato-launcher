# Potato Launcher v1.0.117 — ReShade off even with no DLSS 5 client

- "Turn ReShade off completely on the other clients" now applies when the DLSS 5 client list is empty too. Previously an empty list made every client load full ReShade again.
- Measured: the DLSS 5 loader (ReShade + RenoDX + Streamline) adds ~3.7 ms per frame, so that client holds ~48 FPS against the 60 fps limit and costs ~6% CPU instead of ~2.5%, even with DLSS switched off in-game. Leave the list empty unless you are actually using DLSS; relaunch the client for it to take effect.
