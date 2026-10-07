# Potato Launcher v1.0.131 — Check for updates sees new releases at once

- **Check for updates no longer says "already up to date" right after a release.** It now asks GitHub which release is the latest and downloads that one directly. The shortcut it used before is cached by GitHub for a few minutes after a release is published, so a check made in that window downloaded the previous version and compared it with itself (seen right after v1.0.130). The old shortcut stays as a fallback if GitHub's API cannot be reached.

Includes everything in v1.0.130 (the main keeps 4 cores, honest cache-CCD mode, the placement test judges the main first). After updating: CPU placement on **Auto**, run **Test placements** once somewhere calm.
