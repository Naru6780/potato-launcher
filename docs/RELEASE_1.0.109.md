# Potato Launcher v1.0.109 — ReShade off on non-DLSS clients, safer Multiband and updates

## DLSS 5 clients

- New option in Settings → **DLSS 5 clients**: **Turn ReShade off completely on the other clients** (on by default once at least one DLSS 5 client is chosen). Clients without DLSS 5 then start with no ReShade banner, overlay or effects at all. Untick it to keep ReShade (without add-ons) on those clients.

## Multiband security

- **Pairing confirmation:** when you pair, the connecting PC now shows the other PC's **security code** and asks you to confirm it matches the code shown next to the pairing code on the other PC. This stops a machine on the network from intercepting the pairing. Already paired PCs keep working.
- Connections from outside the local network are dropped before any encryption work, at most 16 connections are handled at once, and slow or oversized requests are cut off (10-second timeouts, 64 KB limit).
- Wrong pairing codes now lock out only the address that guessed, so another device cannot block pairing for your real PC.

## Reliability

- Multiband settings are shared safely between the window and incoming requests; a repeated launch request starts the band only once; closing the Multiband window during a launch asks first and cancels it on both PCs.
- **Updates** now use the same safe installer as rollback: only the executable and assets are replaced, your current version and profile JSON are backed up first, and if anything fails the previous version is restored and you get a message. Updating is refused while a launch queue is running. Only the newest 4 backups are kept.
- **Optimizer:** main-client rules now follow each client's confirmed Character@World instead of the window title, so your main client no longer loses priority on every loading screen.

## Notes

- Both PCs should update to v1.0.109 for the pairing confirmation; existing pairings do not need to be redone.
