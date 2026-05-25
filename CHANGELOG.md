# Changelog

## 1.0.50 - 2026-05-25

- Fixed an unhandled exception when minimizing the app by skipping responsive layout while minimized.
- Made launcher layout metrics safe for minimized or tiny client sizes.
- Added layout regression coverage for minimized and tiny window dimensions.

## 1.0.49 - 2026-05-25

- Replaced the Band Manager member `CheckedListBox` with a theme-aware vertical-scrolling checklist so the account list no longer uses a horizontal scrollbar.
- Kept Band Manager members in compact responsive columns while preserving clean checkbox and account-name alignment.
- Made startup portrait refresh nearly instant when cached portraits are fresh by loading cached icons immediately and only refreshing stale or missing images.
- Refreshed stale or missing portraits concurrently with one settings save at the end instead of refreshing every mapped account one-by-one.
- Added a right-click `Refresh portrait now` action for manually updating a single linked Lodestone profile.
- Added tests for vertical checklist layout and portrait refresh cache policy.

## 1.0.48 - 2026-05-25

- Tightened the Band Manager list spacing so the account checklist gets more usable width.
- Made Band Manager checklist columns responsive to the available panel width instead of using a fixed column size.
- Removed the unnecessary horizontal scrollbar from the Band Manager account checklist.
- Added layout coverage to keep member-list columns fitted during resize.

## 1.0.47 - 2026-05-24

- Added deeper persisted settings cleanup for stale or empty Lodestone profile data.
- Reduced unnecessary repaint churn from repeated launch/loading status updates.
- Removed synchronous loading-overlay refresh calls and narrowed animated background invalidation.
- Polished the help window so it follows the active theme and formats feature sections clearly.
- Added a safer release packaging script that verifies assets and blocks generated portable data from release zips.
- Added tests for settings cleanup and duplicate UI text update throttling.

## 1.0.46 - 2026-05-24

- Fixed a startup crash caused by the custom music volume slider using an unsupported transparent control background.
- Added regression coverage for theme slider palette assignment so the settings drawer can open safely across themes.

## 1.0.45 - 2026-05-24

- Restored animated background bubbles on the main app background.
- Replaced the default music volume trackbar with a compact custom themed slider.
- Styled the volume slider track, fill, thumb, ticks, and focus state from the active theme so dark mode no longer shows a white block.
- Added compact slider metric coverage.

## 1.0.44 - 2026-05-24

- Smoothed splitter dragging by coalescing raw mouse-move events to the latest pending width.
- Precomputed splitter drag bounds once per drag instead of recalculating them on every mouse move.
- Suppressed expensive list repaint churn while dragging, then refreshed the affected content once the drag completes.
- Avoided deep child invalidation during live resize so panels track the pointer more fluidly.

## 1.0.43 - 2026-05-24

- Fixed splitter-drag rendering artifacts where panels could collapse into vertical striped stale-paint regions.
- Kept live resize responsive while making each drag layout update atomic at the resized panel level.
- Added layout clamp coverage for extreme splitter widths.

## 1.0.42 - 2026-05-24

- Made the Accounts/Band Manager resize handle more responsive by removing heavy redraw suspension from live dragging.
- Removed whole-window composited painting because it made nested WinForms resizing feel delayed.
- Limited drag repainting to the affected panel area instead of invalidating child controls during every resize step.

## 1.0.41 - 2026-05-24

- Reduced flicker across themes and state changes by batching redraws during layout, list refreshes, and theme updates.
- Stopped the decorative background from continuously repainting the entire window.
- Enabled smoother double-buffered rendering across the main form and nested controls.
- Increased the maximized account-panel resize range so wide screens can meaningfully expand the Accounts section.
- Replaced the rough native help tooltip with a styled in-app tooltip and cleaned up the Lodestone helper link prompt.

## 1.0.40 - 2026-05-24

- Fixed the help window so feature sections render with proper spacing instead of one compressed paragraph.
- Added a regression test for help text line breaks.

## 1.0.39 - 2026-05-24

- Reworded missing roster portrait guidance so users are asked to link Lodestone profiles instead of refreshing or launching accounts.
- Added a Lodestone character search helper link when setting account profile URLs.
- Reduced noisy account reorder status updates.
- Added startup cleanup for stale `settings.json` values and persisted account-list state.
- Added a `?` help button beside `What's new?` with a user-friendly feature guide.
- Added focused tests for app text and persisted settings cleanup.

## 1.0.38 - 2026-05-24

- Smoothed Accounts/Band Manager splitter resizing by repainting the moved panel region during drag.
- Made the default maximized Accounts panel wider so the roster naturally uses more columns.
- Added a visible insertion marker while dragging roster accounts.
- Changed Settings `Export bands` to open a save dialog; Band Manager `Save` still writes the local `band.json`.
- Enabled multi-column Band Manager member lists on wide layouts.

## 1.0.37 - 2026-05-24

- Account list export/import now carries custom account order and last-connected timestamps.
- Added a horizontal splitter between Accounts and Band Manager so the Accounts panel can be stretched with the mouse.
- Saved the custom Accounts panel width as a UI setting while keeping Band Manager responsive.

## 1.0.36 - 2026-05-24

- Added import mode prompts for account and band imports: `AppendAll`, `AppendNew`, `Merge`, `ReplaceExisting`, and `OverwriteAll`.
- Moved account order and last-connected state out of `settings.json` into portable `accountList.json`, with migration from older settings.
- Added account sorting by selected band and improved roster drag feedback with a live card preview.
- Kept startup portrait refresh from repainting the Band Manager and removed the obsolete per-account Lodestone refresh menu item.
- Changed missing roster tiles to `No Data Found` with clearer Lodestone-link guidance, and simplified account deletion without Shared-mode backups.
- Kept Shared and Instanced account deletion/order cleanup separate so one launch mode does not mutate the other.

## 1.0.35 - 2026-05-24

- Refreshed mapped Lodestone portraits automatically on every app launch.
- Added account drag ordering in both text and roster account views; Band Manager mirrors that same order.
- Added account right-click sorting by name and by last connected.
- Added account right-click deletion with `accountsList.json` backup in Shared mode and safe BAT deletion in Instanced mode.
- Removed obsolete Band Manager member drag code so account order is the single source of truth.

## 1.0.34 - 2026-05-24

- Removed the permanent Band Manager rename text box.
- Added a band right-click `Set name` action that opens a small naming prompt.
- Expanded the Band Manager member list into the freed rename-field space.

## 1.0.33 - 2026-05-24

- Removed the Settings `Refresh account icons` button; per-account refresh remains in the account right-click menu.
- Added a Settings `Export bands` action next to `Import bands`.
- Changed Band Manager `Save` / `Export bands` to write `band.json` automatically beside `settings.json`.
- Reduced bottom button flicker by using a buffered Band Manager button row.

## 1.0.32 - 2026-05-24

- Removed the obsolete `Launch selected` button now that roster double-click launches individual accounts.
- Moved the loading screen into the Band Manager area so the account roster stays visible during launches.
- Improved small-window Band Manager button spacing with a wrapping button row.
- Changed Band Manager account rows to show character names instead of `account id - character` labels.
- Added Shared account metadata export/import that merges Lodestone mappings without wiping existing `accountsList.json` entries.
- Renamed `New band` to `Add Band` and added `band.json` band save/import support.

## 1.0.31 - 2026-05-24

- Backfilled XIVLauncher's Shared-mode account metadata when a Lodestone profile URL is manually assigned from the right-click menu.
- Added timestamped `accountsList.json` backups before writing `ChosenCharacterName`, `ChosenCharacterWorld`, and `ThumbnailUrl`.

## 1.0.30 - 2026-05-24

- Improved automatic Lodestone profile discovery by reusing known surnames and worlds from accounts that already have profile data.
- Reduced the need to paste profile URLs manually after one matching profile has established the account group's world and surname pattern.

## 1.0.29 - 2026-05-24

- Added right-click account menu actions to open, refresh, or set a Lodestone character profile URL.
- Changed icon refresh to fetch portraits directly from Lodestone profile pages when a profile ID or URL is known.
- Improved unmapped account discovery by using known Lodestone worlds and strict name candidates instead of requiring every account to be launched first.

## 1.0.28 - 2026-05-24

- Made the main Potato Launcher window resizable with a sensible minimum size.
- Added responsive layout behavior for account roster, band manager, status pill, settings drawer, loading overlay, launch picker, and news panel.

## 1.0.27 - 2026-05-24

- Replaced the oversized Windows icon list with a compact custom character roster grid.
- Added roster tile selection, double-click launch, themed selection styling, and strict refresh-needed tile states.
- Cached full-body Lodestone portraits alongside face portraits for future character card views.

## 1.0.26 - 2026-05-24

- Added Settings support for switching the account list between text and Lodestone portrait icons.
- Added automatic `Character@World` mapping when a launched client reaches the ready title.
- Added strict Lodestone icon refresh and local account portrait caching without fake fallback icons.

## 1.0.25 - 2026-05-23

- Moved the main-window music mute toggle beside the Kill FFXIV button so it no longer overlaps the mascot area.

## 1.0.24 - 2026-05-23

- Added a main-window music mute toggle for quick access.
- Kept the Settings mute checkbox synced with the main mute button.

## 1.0.23 - 2026-05-23

- Updated the loading status wording while waiting for each character title.

## 1.0.22 - 2026-05-23

- Added a configurable band launch cooldown with a default minimum of `0` seconds.
- Separated queue pacing from loading completion so clients can start on cooldown while the loading screen waits for every `Character@World` title.

## 1.0.21 - 2026-05-23

- Replaced character-selection screenshot detection with FFXIV window-title readiness.
- Advanced band queues after the new game window title stabilizes as `Character@World`.

## 1.0.20 - 2026-05-23

- Changed band queue readiness to wait for the FFXIV character-selection screen.
- Kept TCP connection checks as supporting status while the visual readiness gate waits for character selection.

## 1.0.19 - 2026-05-23

- Removed unused legacy GIF/loading splash helper classes.
- Kept the current launcher feature set intact while trimming dead internal code.

## 1.0.18 - 2026-05-23

- Removed arbitrary in-game wait and old launch cooldown countdown.
- Added client-aware queueing that tracks the newly launched `ffxiv` / `ffxiv_dx11` process.
- Advanced band launches only after the new game client has an established TCP connection.

## 1.0.17 - 2026-05-23

- Added an extra in-game wait setting after XIVLauncher handoff.
- Kept the loading screen and loading music alive during the extra wait.

## 1.0.16 - 2026-05-23

- Added `Stop music when all loaded`.
- Added music volume control.

## Earlier 1.0.x Highlights

- Added first-run launch method selection.
- Added separate Instanced and Shared band managers.
- Added theme folders, custom backgrounds, mascot/loading GIF assets, and per-theme music playlists.
- Added FFXIV news overlay.
- Added GitHub release updater with changelog popup.
