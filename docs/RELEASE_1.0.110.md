# Potato Launcher v1.0.110 — keep every client at its FPS cap

## Optimizer: Keep every client at its FPS cap (on by default)

Built for running many clients that must all hold their frame cap (for example 16 × 60 FPS). It never lowers any client's frame rate.

- **No more throttled background windows.** Windows 11 throttles minimized and covered windows (timer resolution and EcoQoS), which makes FFXIV's frame limiter oversleep and drops those clients below 60 FPS. Potato now opts every client out.
- **The client you are playing comes first.** The foreground client and your main clients get Above Normal CPU priority and Above Normal GPU scheduling priority. All other clients stay at Normal (never Below Normal), so browsers and other apps cannot take their CPU time.
- **RAM goes where it matters.** Background clients get low memory priority: when RAM runs short, Windows reclaims theirs first, not the played client's. Working-set trimming never touches the played client.
- Toggle: Optimizer window → top right, **Keep every client at its FPS cap**. Turning it off (or closing Potato) restores Windows' defaults.

## Notes

- CPU affinity "lanes" stay optional; on single-CCD CPUs (e.g. Ryzen 7 9800X3D) leaving every client on all cores is best.
- If you use Process Lasso, exclude `ffxiv_dx11.exe` from ProBalance so it does not demote clients.
