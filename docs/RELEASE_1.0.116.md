# Potato Launcher v1.0.116 — v1.0.115 plus a TextInputHost watchdog

Includes everything from v1.0.115 (the in-game frame limit that holds while covered or minimized, the Cap column and diagnostics).

- Windows' TextInputHost (touch keyboard / IME host) kept getting stuck at ~5% CPU, the cost of two game clients. Potato now ends it after 30 s of spinning; Windows recreates it idle. Off switch: resetSpinningInputHost in optimizer.json.
