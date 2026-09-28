# Changelog

All notable changes to Cover Shuffle are documented here. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
follows [Semantic Versioning](https://semver.org/) — see the Versioning
section in `README.md` for what bumps each part.

> **Note:** `1.1.0` was never released — the version field jumped straight
> from `1.0.0` to `1.2.0` in an early commit, skipping a proper minor bump for
> an intermediate change. That gap is intentional and not a missing entry.

## [Unreleased]

### Added

- Maintenance now scans for managed games (games with at least one stored
  cover) that Playnite currently reports as not installed, surfaced as a
  fourth card in the Maintenance window. Confirming selected candidates
  restores each game's original Playnite cover first and only removes Cover
  Shuffle's own data once that restore is verified — the same cleanup
  already run automatically by Playnite's uninstall event. This scan also
  runs once at Playnite startup and raises a notification (subject to your
  notification preference) when it finds candidates.

### Fixed

- Database saves now use a crash-safe file replace instead of a
  delete-then-move sequence, so an interrupted save can no longer leave the
  plugin with no database file at all.
- Cover file paths read back from the database or an imported backup are now
  validated to stay within plugin-owned storage before being used, closing a
  path-traversal gap a corrupted or tampered record could otherwise exploit.
- Replacing a cover's file now saves the new file before removing the old
  one, so a failed replace can no longer destroy an existing cover.
- Oversized images are now rejected before the expensive full image decode
  runs, rather than after, bounding worst-case memory use for a small file
  with an enormous declared resolution.

### Removed

- WEBP is no longer listed as a supported local-import format. GDI+ (the
  underlying decoder on most Windows installs) cannot reliably decode WEBP,
  so the previously advertised WEBP-to-PNG conversion did not actually work
  for real WEBP files. Supported formats are now PNG, JPG, JPEG, BMP, and GIF.

## [1.3.0] - 2026-09-11

### Added

- Local cover import: an "Add Local Covers" window supporting drag-and-drop
  or file browsing, with per-file status (duplicate, invalid, oversized, or
  needing conversion/resizing) shown before import.
- Centralized cover import policy (`CoverImportPolicy`) and an image
  normalization service that converts WEBP to PNG and downsizes oversized
  images automatically.
- Content-based duplicate detection for imported covers via a shared hashing
  utility.
- Startup maintenance check that detects missing/invalid cover files and
  surfaces recovery options in the manager UI.
- Missing-cover indicators and replace/restore options in the cover
  management UI.

## [1.2.0] - 2026-09-10

### Added

- Manual cover selection ("Use This Cover") that jumps directly to a chosen
  cover, tracked separately from a randomized "Shuffle Now" pick via a new
  shuffle trigger distinction.
- Per-game settings overrides that inherit individual global defaults
  instead of requiring a full settings snapshot, plus a "Reset to Global
  Defaults" action.
- A game-installation check so scheduled shuffles skip games that aren't
  currently installed.
- A dedicated shuffle engine abstraction (`IShuffleEngine`,
  `IShuffleRandomizer`) implementing randomized, no-immediate-repeat cycling,
  with a pluggable randomizer for testability.
- SteamGridDB multi-match handling: when a search matches more than one
  game, the user picks the correct one instead of the first result being
  assumed.
- Configurable notification preferences (silent / errors only / every
  shuffle), applied per game or globally.

### Changed

- SteamGridDB search moved from a standalone window to a `UserControl`
  embedded in Playnite's own UI, with the game title pre-filled from the
  selected game.
- `ManageCoversWindow` moved to a `UserControl` for the same reason, and
  gained a status indicator for Cover Shuffle's enabled/disabled state.
- Cover cards gained accessible descriptive text for screen readers.

## [1.0.0] - 2026-09-10

### Added

- Initial release: per-game cover pools (up to 10 covers), local file
  import, SteamGridDB search/download, and Playnite metadata (cover/
  background/icon) as cover sources.
- Randomized, no-immediate-repeat shuffle cycling with persisted shuffle
  state.
- Per-game and global scheduling with due-shuffle handling at Playnite
  startup, and optional shuffle on game launch.
- Cover ownership tracking with original-cover restoration.
- New-game automation (Do Nothing / Ask / Automatic).
- Cover Shuffle Manager with bulk operations and configuration
  import/export.
- Maintenance scanning for orphaned files and stale cache entries.
- SteamGridDB API key validation.
