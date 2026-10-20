# FFXIV compatibility verification

## 2026-10-21

- Installed game version: `2026.10.16.0000.0000`.
- Executable SHA-256: `46A52AB1DBE80B9A8D6C4D4531BF66693904200C5135FACACD053EF28A5DE14A`.
- Reference: https://github.com/aers/FFXIVClientStructs/tree/dd57986b8287e7c6ed9c27198f8c76cb04a81dfe
- Current upstream definitions retain the reader's PlayerState loaded/name/entity/content fields, GameObject name/entity/kind fields, Character ContentId (0x2358) and HomeWorld (0x2362), GameMain connection/load/territory fields (0x40FE/0x4100/0x4108), and condition flags (45/51/53/60).
- Framework frame counter/rate offsets remain 0x16D0/0x17CC; Device limiter fields remain 0xA8/0xAE.
- Offline executable validation uses the production resolver: exact hash allowlist, unique matches for all six signatures, and image-relative address bounds. Set POTATO_OFFLINE_GAME_EXE to the installed executable when running tests.
- No debugger, injection, or game-memory writes used. Runtime character-selection/loading/in-world transitions still require a user smoke test.
- Visible titles now contain only the confirmed character name; world-qualified identities remain separate for optimizer matching. Unconfirmed clients retain the neutral FINAL FANTASY XIV title.
