# Cover Shuffle

A Playnite extension that lets you keep a small pool of alternate cover
artworks per game (up to 10) and rotate through them automatically, or on
demand — without a custom Playnite theme.

## Features

- **Per-game cover pool** — up to 10 covers per game, added from:
  - a local image file,
  - [SteamGridDB](https://www.steamgriddb.com/) (requires a free API key), or
  - artwork Playnite already has for the game (its cover, background, and icon) —
    works without any API key.
- **Randomized, no-immediate-repeat shuffling** — each shuffle draws from a
  randomized cycle of the game's enabled covers; every cover is shown once
  before the cycle reshuffles, and the first cover of a new cycle is never
  the same as the last cover of the old one (when more than one cover is
  available).
- **Safe artwork ownership** — the game's original cover is captured the first
  time Cover Shuffle takes control of a game, and can always be restored.
  Removing a cover from the pool never deletes the underlying file.
- **Shuffle on startup** — every time Playnite starts, each enabled,
  installed game gets a new cover (a global setting; on by default).
  Restarting Playnite twice in a row shuffles twice. A cover whose file has
  gone missing is skipped rather than applied, so a manually deleted file
  never leaves a broken cover behind.
- **Shuffle on game launch** — optionally rotate a game's cover the moment
  you launch it (a global setting).
- **New-game automation** — choose whether newly installed games are left
  alone, prompted about, or automatically prepared for Cover Shuffle.
  "Automatic" pulls in the game's existing Playnite cover art so it has
  something to shuffle right away, and never contacts SteamGridDB on its own.
- **Notifications** — optionally surface automatic shuffle activity (success,
  or failure) as a Playnite notification. Configurable per game or globally:
  silent, errors only, or every shuffle. A startup pass posts one summary
  (e.g. "rotated the covers for 12 games") rather than one per game.
- **Cover Shuffle Manager** — a searchable list of every game Cover Shuffle
  manages, with bulk enable/disable/interval actions and configuration
  import/export.
- **Maintenance** — scan for orphaned cover files, cover records whose file
  went missing, cached provider images, and managed games Playnite now
  reports as not installed; nothing is deleted without an explicit,
  confirmed action.

## Installation

1. Download the `.pext` package (see [Development](#development) if you're
   building it yourself).
2. Double-click it with Playnite running — Playnite will prompt to install
   it — or use **Add-on menu → Extensions → Install from file** in Playnite.
3. Restart Playnite when prompted.

No configuration is required to start using local files or Playnite's own
artwork as covers. SteamGridDB is optional — see below.

**Upgrading:** installing a newer version over an existing one preserves all
saved configuration, covers, and shuffle state; nothing needs to be
reconfigured. Cover Shuffle's data lives entirely in its own storage folder
under Playnite's extension data directory, separate from Playnite's own
library data, so upgrading or reinstalling the plugin never touches your
games or their original artwork.

## Configuration

Open Playnite's **Add-on menu → Extensions → Cover Shuffle** (or find it in
Playnite's settings under Extensions) to set the defaults every game follows
unless it has its own override:

| Setting | What it controls |
|---|---|
| Enable Cover Shuffle | Default enabled state for newly-configured games |
| Shuffle on Playnite startup | Give every enabled, installed game a new cover each time Playnite starts |
| Shuffle on game launch | Also shuffle the cover the moment you launch a game |
| Shuffle interval (hours) | Sets the "next shuffle" time shown in the Manager. It doesn't currently change *when* covers shuffle: that's startup and/or game launch |
| New game behavior | Do nothing / Ask / Automatic — see [Usage](#usage) |
| Notifications | Silent / Errors only / Every shuffle |
| SteamGridDB API key | Optional — see below |

Any game can override its enabled state or interval individually from its
**Manage Covers** window without touching these global defaults. Each setting
is overridden independently: giving one game its own interval doesn't freeze
any of its other settings, so if you later change the global notification
preference (for example), that game keeps following it live unless you've
overridden that setting too. Click **Reset to Global Defaults** in a game's
Manage Covers window to clear all of its overrides and have it follow every
current global default again.

A shuffle never repeats the cover that's currently showing (as long as the
game has more than one enabled cover); this is always on, not a setting.

Intervals are entered in hours (e.g. `24` or `1.5`; a comma decimal like `1,5`
also works if that's your Windows number format) and must be between one
minute and one year. The same rule applies in the settings page, a game's
Manage Covers window, and the Manager's bulk **Set Interval**. An invalid value
is shown as an error and blocks saving; it is never silently replaced with a
default.

## SteamGridDB setup

SteamGridDB is entirely optional. Local files and Playnite's own artwork
work with no setup at all; only the **Add Cover → SteamGridDB** menu action
needs it.

1. Create a free account at [steamgriddb.com](https://www.steamgriddb.com/).
2. Generate an API key at
   [steamgriddb.com/profile/preferences/api](https://www.steamgriddb.com/profile/preferences/api).
3. Paste it into Cover Shuffle's settings → **API key**, then click
   **Validate** to confirm it's accepted before relying on it.

The key is never logged, never shown in an error message, and never stored
anywhere outside Playnite's own settings storage. If a search matches more
than one game (e.g. searching "Fallout"), you'll be asked which one you
meant before covers are shown — nothing is guessed silently.

## Usage

Right-click a game in Playnite → **Cover Shuffle**:

| Action | What it does |
|---|---|
| Enable / Disable Cover Shuffle | Turns scheduled shuffling on/off for this game |
| Add Cover → Local File | Opens "Add Local Covers": drag & drop or browse for one or more images (PNG, JPG, JPEG, BMP, GIF), preview and remove candidates before importing, and see per-file status (duplicate, invalid, over the 20 MB size limit, or will be resized). Images larger than 3000px on a side are scaled down automatically. Duplicate images (by content, not filename) are rejected automatically. |
| Add Cover → SteamGridDB | Search and browse SteamGridDB covers visually |
| Add Cover → Playnite Metadata | Pull in the game's existing cover/background/icon |
| Shuffle Now | Immediately advances to the next cover in the pool |
| Manage Covers | View covers, current status, and next shuffle time; choose a specific cover, or remove covers from the pool |
| Restore Original Cover | Puts the game's original cover back |

Inside **Manage Covers**, each cover card has a **Use This Cover** button to
jump directly to that cover instead of letting the randomized shuffle pick
one. This is a manual override: it still updates the game's cover, usage
stats, and next scheduled shuffle time, but it's recorded separately from a
normal "Shuffle Now" pick so the two don't get conflated.

The main Playnite menu has a **Cover Shuffle Manager...** entry for managing
many games at once: search, bulk enable/disable/interval, configuration
import/export, and maintenance (see below).

### New-game automation

Set in Cover Shuffle's settings under **New game behavior**:

- **Do nothing** — newly installed games are left alone.
- **Ask** — you're prompted whether to enable Cover Shuffle for it.
- **Automatic** — Cover Shuffle enables itself for the game and pulls in
  whatever cover art Playnite already has locally, so the game has
  something to shuffle right away. This never queries SteamGridDB on its
  own; that always requires you to explicitly use **Add Cover → SteamGridDB**.

A game is only ever treated as "new" once — reinstalling a game Cover
Shuffle has already seen (even one you later disabled) does not reapply
this behavior.

### Importing/exporting configuration

The Manager's **Export** button writes a `CoverShuffle.json` file plus the
referenced cover images into a folder you choose. **Import** reads that same
folder back in. This is meant for restoring your configuration on another
Playnite installation that shares the same library (e.g. a synced library on
a new PC) — Playnite's own game IDs need to match for the restored
configuration to line up with the right games.

### Maintenance

From the Cover Shuffle Manager, **Maintenance** scans for:

- cover files on disk no longer referenced by any cover record (orphans),
- cover records whose file is missing from disk,
- cached SteamGridDB images, and
- managed games (games with at least one stored cover) that Playnite
  currently reports as not installed.

The scan itself never changes anything; each cleanup action requires you to
review the findings and confirm it separately. For uninstalled games,
**Restore & remove selected** restores each game's original Playnite cover
first and only removes Cover Shuffle's own data for it once that restore is
verified — the same cleanup that already runs automatically when Playnite's
own uninstall event fires for a game. Cover Shuffle also runs this scan once
at startup and raises a notification (subject to your notification
preference) if it finds any candidates, so you don't have to open
Maintenance manually to notice.

## Troubleshooting

**A game's cover isn't shuffling.**
Check its **Manage Covers** window: it shows whether Cover Shuffle is
enabled for that game and how many covers it has. A game needs at least one
enabled cover, to be enabled itself, and to be installed; it then shuffles
when Playnite starts and/or when you launch it, depending on your settings.

**"This game has no covers configured yet."**
Add at least one cover via the game's **Add Cover** submenu before
shuffling — scheduled shuffles skip games with an empty pool rather than
failing loudly, but "Shuffle Now" will show you this message directly.

**"None of this game's covers could be found on disk."**
One or more cover files were deleted or moved outside the plugin (manually,
by another tool, antivirus quarantine, etc.). Open **Manage Covers** or run
**Maintenance** from the Cover Shuffle Manager to find and clean up the
affected records; the plugin never applies a reference to a file it can't
find, so you'll see this message instead of a broken cover image.

**"SteamGridDB API key is not configured." / "...rejected the configured
API key."**
Add or fix the key in Cover Shuffle's settings, then use the **Validate**
button to confirm SteamGridDB accepts it before searching again.

**"Could not reach SteamGridDB."**
A network problem (offline, DNS, firewall, SteamGridDB itself being down).
Everything that doesn't require SteamGridDB — local files, Playnite
metadata, shuffling already-added covers, enable/disable, restore — keeps
working normally while it's unreachable.

**A cover I added doesn't look right / the file seems corrupted.**
Cover Shuffle validates that a file is a real, decodable image before
adding it to the pool; an invalid file is rejected up front with a clear
message rather than being stored and failing later.

**The plugin's database file looks corrupted or Cover Shuffle lost its
configuration after a crash.**
Cover Shuffle never lets a corrupt data file crash the plugin. If its
database can't be parsed, the original file is preserved next to itself
(named with a `.corrupt-<timestamp>` suffix) for manual recovery, and the
plugin starts fresh rather than losing the ability to load at all.

**Something else looks wrong.**
Check Playnite's own extension log (`extensions.log` in the Playnite
folder) for lines starting with `PluginCoverShuffle#CoverShufflePlugin` —
Cover Shuffle logs warnings and errors there rather than showing raw
exceptions to you.

## Known limitations

- Covers only change automatically at Playnite startup or game launch —
  never mid-session on a timer. The interval setting doesn't affect this.
- The startup shuffle only applies to installed games; an enabled game that
  isn't installed is skipped until it's installed again.
- The Playnite Metadata provider only picks up artwork already downloaded to
  a local file, not a bare remote URL some library plugins store instead.
- Configuration import/export is intended for the same Playnite library
  (matching game IDs); it does not remap covers to a different library.

## Development

- Target framework: `net462` (matches Playnite's own runtime).
- Build: `dotnet build PluginCoverShuffle.csproj` (also copies the DLL next
  to `extension.yaml`, where Playnite expects it, and to
  `bin/<Configuration>/`). `Playnite.SDK.dll` is deliberately excluded from
  the build output — Playnite provides it at runtime.
- Package a release: `dotnet build PluginCoverShuffle.csproj -c Release -t:PackageExtension`
  produces `bin/Package/CoverShuffle_<version>.pext` containing exactly
  `extension.yaml`, `PluginCoverShuffle.dll`, and `Newtonsoft.Json.dll` — no
  debug symbols, no Playnite SDK assembly, nothing from `Tests/`.
- Tests: `dotnet test Tests/PluginCoverShuffle.Tests.csproj` — all tests run
  against fakes; none require a live Playnite installation, network access,
  or a SteamGridDB API key.
- See `CLAUDE.md` for the engineering ruleset this project was built against.

## Versioning

Cover Shuffle follows [Semantic Versioning](https://semver.org/)
(`Major.Minor.Patch`):

- **Major** — a breaking change: persisted data that can't be migrated
  forward, or a user-visible behavior change that isn't backward compatible.
- **Minor** — new functionality that's backward compatible (a new cover
  source, a new manager feature, etc.).
- **Patch** — bug fixes only, no new functionality.

When bumping the version, update all of the following together — they must
always agree:

- `<Version>` / `<AssemblyVersion>` / `<FileVersion>` in
  `PluginCoverShuffle.csproj`
- `Version` in `extension.yaml`
- "Current project version" in `CLAUDE.md`
- a new entry at the top of `CHANGELOG.md`

The plugin's internal on-disk data schema (`CurrentSchemaVersion` in
`CoverShuffleRepository.cs`) is versioned separately and does not need to
change in lockstep with the product release version — it only bumps when the
persisted data's shape actually changes.
