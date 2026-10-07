# Potato Launcher v1.0.119 — CPU lanes removed

- **CPU lanes (advanced) is gone**, with its allocator modes, processor counts, reservations, Live/Planning switch, the Affinity/Planned columns and the Optimize CPU / Restore / Rescue buttons.
- Why: on a single-CCD CPU every core is the same, so pinning cannot make the main client faster. It only walls the followers into fewer cores, where each client's threads queue behind each other (measured 48-58 FPS with lanes on vs 59-60 without). "Keep every client at its FPS cap" already gives the played and main clients Above Normal priority, which is what actually protects them.
- Existing optimizer.json files load as before; the old lane keys are ignored.
- A follower that stays unresponsive for a minute still raises one notification.
