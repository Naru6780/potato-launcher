# Potato Launcher v1.0.129 — Test placements judges steadiness

- **Test placements now judges how steady each client is, not just its average.** It records every client's FPS each second and ranks placements by their worst seconds (5th percentile): a client that averages 58 but dips to 50 no longer counts as "at 60". The main's worst seconds come next, CPU only decides after that.
- **A test during a changing scene is marked inconclusive.** If the game itself gets busier or quieter mid-test (the same placement measuring more than 5 points apart between rounds), your current placement is kept and you are asked to retest somewhere calm.
- **Every placement strategy is now checked automatically** across both CPU layouts, 2-16 clients and every load level: the main always keeps at least 2 cores of its own or a whole CCD, and no follower straddles two CCDs or lands on the main's cores.
- The CPU placement dropdown's tooltip explains each option with what was measured. Earlier test results are discarded (measured with the old scoring).
