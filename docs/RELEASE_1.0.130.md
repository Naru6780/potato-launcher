# Potato Launcher v1.0.130 — The main comes first

- **The main keeps 4 cores of its own.** It gives one up only while the followers would otherwise have less than 15% spare over what they actually use, and never goes under 2 cores or shares every core (that measured worst of all: 36 FPS). Up to 1.0.129 the followers were required to keep 50% spare, which on a 9950X3D running 16 clients left the main 2 cores at 41-45 FPS while 8 threads sat idle. Replayed on that recording, 1.0.130 gives the main 0-7 and the followers 8-15 and 16-31.
- **No more re-pinning on the line.** A core the main holds is only given up once the followers would drop below 5% spare, and comes back after a minute of room, so a load hovering on the threshold no longer moves every client back and forth.
- **A restart no longer mis-sizes the main.** Follower load counts only after 5 s of readings; the first one-second snapshot after restarting Potato used to decide the main's cores.
- **"Main gets the cache CCD" says what it does.** When the followers do not fit in the other CCD (about 10 followers on a 9950X3D), it runs as "Main gets its own cores" and the status line says so. Test placements skips it in that case instead of measuring the same placement twice.
- **Test placements judges the main first:** steady at the target beats not; while neither is, the higher main wins; then clients at target; then CPU. Earlier results are discarded (scoring v8). The status line shows the main's current core count.

After updating: leave **CPU placement** on **Auto**, run **Test placements** once somewhere calm, then **Export diagnostics** if anything still looks wrong.
