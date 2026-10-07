# Potato Launcher v1.0.126 — steadier CPU placement, a test you can trust

- **No more re-pinning every few seconds.** With the followers' load sitting on a threshold, the main's reserved cores flipped (for example 0-7 and 0-5) and every client was re-pinned each time, which shows up as FPS dips. The main now gives cores up as soon as followers need them, but only takes them back once followers clearly have room. Same for "Main gets the cache CCD".
- **Test placements notices outside load.** If other programs' CPU use changes by more than 3 points during the test (a compile, a video export, an analysis tool), the result is marked inconclusive and your current placement is kept.
- **Ties keep "Main gets its own cores".** When the placements measure within the noise, the test keeps the default instead of falling back to no pinning. Results stored by earlier versions are discarded, so Auto goes back to the default until you test again.
