# Cover Shuffle — Claude Engineering Rules

> Mandatory project guidance. This is an existing, working extension — not a
> blank project. The existing implementation is the source of truth.

## 1. Project Identity

- **Repo**: `Kyerstorm/Cover-Swapper` · **Product**: Cover Shuffle
- **Assembly/Namespace**: `PluginCoverShuffle` · **Target**: Playnite Desktop
- **Framework**: `net462` · **UI**: WPF · **Version**: `1.3.0`

Lets users maintain up to 10 alternate covers per game and rotate the
Playnite cover manually, on a schedule, or on game launch. Sources: local
files, SteamGridDB, existing Playnite metadata/artwork. The plugin owns
copies of managed artwork and retains enough info to restore the original.

## 2. Existing-Repository Rule — Highest Priority

Inspect before implementing. The roadmap (§54) describes desired outcomes,
not permission to rewrite working code.

If a requested feature already exists: locate it, read it and its tests,
decide if it satisfies the request, keep what's correct, modify only the
deficient parts, add tests where needed, avoid architecture churn without a
demonstrated problem. Never assume something is missing because it's on the
roadmap; never replace working code with a "cleaner" hypothetical design.

Before touching an area, answer: What implements this now? What tests cover
it? What does the README promise? What's actually missing/broken? What's
the smallest safe change?

## 3. Current Capabilities (assume present unless inspection proves otherwise)

Per-game cover pools (max 10) · local import · SteamGridDB search/download ·
Playnite artwork import · cover hashing/validation · ownership + original-
cover restoration · persistent plugin-owned data · cover enable/disable ·
randomized no-immediate-repeat shuffling · persisted shuffle state · per-
game/global scheduling · due-shuffle handling at startup · optional shuffle
on launch · new-game automation (Do Nothing/Ask/Automatic) · configurable
notifications · Cover Shuffle Manager with bulk ops + import/export ·
maintenance scanning/cleanup · SteamGridDB key validation · provider/cache
abstractions · separate test project · `.pext` packaging.

Don't reimplement these as if missing. The README is part of the product
contract — if code and README disagree, investigate which is authoritative.

## 4. Verified Current Limitations

1. Due scheduled shuffles apply at Playnite startup, not continuously.
2. Playnite metadata artwork support is strongest for artwork already local;
   bare remote URLs aren't fully supported.
3. Import/export assumes the same Playnite library (matching game IDs).

Record newly discovered limitations rather than ignoring them. Don't claim a
limitation is fixed unless implementation and tests prove it.

## 5. Hard Rule: Phased Development

Work only on the phase given. A phase may introduce the minimum abstraction
a future feature needs, but must not silently implement that future
feature. Example — correct: add a scheduler abstraction while building live
scheduling. Incorrect: also add seasonal/AI artwork, stats, or new
providers while fixing scheduling. If no phase is specified, pick the
smallest coherent phase yourself rather than expanding scope.

## 6. Phase Completion Gate

A phase is done only when: it builds; existing functionality still works;
requested functionality is implemented (not stubbed); relevant tests exist
and pass; no new unjustified compiler warnings; no TODO standing in for
claimed-complete functionality; persistent data stays compatible or is
migrated; UI works in both Playnite themes; no unrelated future feature
snuck in; the change was reviewed for regressions.

Every completion report needs: Summary, Files added/modified, key
decisions, tests added/changed, build result, test result, known
limitations, deferred work, manual Playnite test steps.

## 7. Priority Order (when requirements conflict)

1. User data safety 2. Playnite compatibility 3. Correctness 4. Existing
user configuration 5. Requested phase 6. Reliability 7. Maintainability
8. Performance 9. UX convenience 10. Future extensibility

Never sacrifice data safety or Playnite compatibility for polish.

## 8. Architecture

Preserve the existing layering:

```
Playnite Integration → Application Services → Domain/Core → Infrastructure
```

UI calls services/view models, never persistence directly. Domain/shuffle
logic knows nothing about WPF or SteamGridDB. SteamGridDB code knows
nothing about WPF. Playnite SDK types shouldn't leak into domain logic
without strong reason. Add abstractions for testability/separation/real
future need — not for theoretical purity.

## 9. `Plugin.cs` and Lifecycle

Composition root only — lifecycle, service wiring, Playnite event
registration, menus, settings, disposal. No business logic; substantial
logic belongs in a service. Register event handlers exactly once; dispose/
unregister properly. Reload and shutdown must leave no running workers,
timers, HTTP calls, or subscriptions.

## 10. Domain Model

- **Game** — referenced by stable Playnite ID; never duplicate the full
  Playnite game object in plugin storage.
- **Cover** — CoverId, GameId, Source, SourceId, LocalPath, Hash, AddedAt,
  LastUsedAt, UsageCount, IsEnabled. Reuse this model, don't add a parallel one.
- **Settings** — global defaults, optional per-game overrides.
- **Shuffle state** — CurrentCoverId, LastShuffleAt, NextShuffleAt,
  cycle/order state. Never reconstruct scheduling state from UI state.

## 11. Cover Limit

Max **10 covers/game**, enforced centrally — never scatter literal `10`
through business logic, never make it configurable without an explicit
phase for that, never let bulk actions/imports/providers/UI bypass it.

## 12. Cover Ownership & User Data Safety — Critical

Never destructively overwrite original artwork without retaining enough to
restore it. Capture the original the first time Cover Shuffle takes
control. Disabling Cover Shuffle must not destroy the original. Removing a
cover from the pool must not delete its file unless the user explicitly
runs a safe cleanup action. Never delete Playnite's original artwork as
routine housekeeping. Never assume an artwork reference is a plain
filesystem path — use Playnite-supported artwork APIs.

## 13. Shuffle Engine

Behavior is randomized, no-immediate-repeat. Not `current + 1`; not
unrestricted random that can repeat immediately. Semantics: gather enabled
covers → drop invalid/missing → build a randomized cycle → consume each
cover once → reshuffle a new cycle when exhausted → with >1 candidate,
never start the new cycle with the cover just shown.

```
Pool: A B C D E
Cycle 1: C A E B D
Cycle 2: B D A E C
```

Exact sequence isn't contractual; the invariants are. Must be independently
testable; don't scatter `Random` calls — abstract it if needed, without
coupling tests to specific values. Must stay correct for: 0/1/2 covers,
disabled covers, missing files, removed/newly-added covers, current cover
removed, Playnite restart, persisted cycle state.

## 14. Scheduling

Use real timestamps (LastShuffleAt/NextShuffleAt), not "add interval on
every startup." At startup, apply due work once — don't replay every missed
interval. No duplicate scheduling work on reload. A game with no valid
enabled covers must be skipped safely, never given a broken artwork
reference. An interval change recalculates and persists the next run. All
internal time math uses UTC; convert to local only for display. Future live
scheduling needs proper cancellation/lifecycle and must not block the UI
thread.

## 15. Settings Inheritance

```
Global default → Game override (if present) → Effective settings
```

A game should be able to inherit individual settings rather than needing a
full snapshot. If the current implementation is still a full per-game
snapshot, don't silently redesign it in an unrelated phase — introduce real
inheritance only as its own configuration-focused change, with migration/
compatibility for existing data. Validate settings before applying; invalid
intervals must not crash the plugin. Don't store transient runtime state as
a user setting.

## 16–19. SteamGridDB — Architecture, Matching, Network, Caching

- All API access lives in the provider/client layer — no scattered HTTP
  calls, no raw HTTP from UI. Flow: search/match → selected game → artwork
  retrieval → download/cache → normalized CoverAsset/import.
- Never hard-code, commit, log, or leak credentials — including in
  exceptions or UI diagnostics.
- Don't assume the first search result is correct; let the user resolve
  ambiguous matches. Matching may use name/platform/year/developer/known
  IDs. Skip complex scoring without tests and a product reason. A
  high-confidence auto-match is fine only if the user can still correct it.
- Use async I/O. Handle gracefully: no key, invalid/rejected key, timeout,
  DNS/network failure, server error, rate limiting, malformed response,
  download failure. Bounded retry/backoff for transient failures only —
  never retry auth failures indefinitely. Provider failure must never break
  normal offline shuffling.
- Use the existing cache abstraction (don't add a second one). Cache is an
  optimization, never a dependency — previously imported covers must work
  offline. Invalidation/cleanup is explicit and testable; no unbounded
  growth without a maintenance strategy.

## 20–22. Artwork Import & File Safety

- **Playnite artwork provider**: normalize into the plugin's owned cover
  representation; existing local artwork must work offline; if importing
  remote URLs, the downloaded copy must become plugin-owned and validated
  before entering the pool. Never modify Playnite artwork directly.
- **Local file import**: validate the image, use controlled plugin storage,
  avoid unsafe filenames/paths, dedupe where infra supports it, respect the
  10-cover limit, never delete the source file. Future drag-and-drop/multi-
  select must reuse the same import service.
- **File safety**: never trust remote/source-derived identifiers as raw
  paths; use stable IDs + validated extensions; no path traversal; never
  execute downloaded files; validate image contents before use. If a
  registered cover's file is missing: don't apply the broken path to
  Playnite, surface the problem, allow maintenance/recovery, keep the
  record until the user decides.

## 23–25. Persistence, Schema, Recovery

State must survive Playnite restart, plugin reload, and computer restart,
via the existing repository/persistence abstraction — UI/providers never
edit JSON directly. Writes should be interruption-safe where practical.
Don't replace the persistence system without evidence it's necessary.

Schema changes: increment the version, write a deterministic migration,
preserve old data, test old→new, ensure a failed migration doesn't destroy
the original, document it. No destructive migrations; move toward an
explicit migration pipeline rather than ad-hoc compatibility branches.

A corrupt database must not crash Playnite; preserve the corrupt source for
recovery where supported. If starting clean after corruption, say so
plainly — never claim recovery that didn't happen. Prefer atomic-replace
writes. Any future backup/recovery feature builds on the existing model.

## 26–30. UI Architecture & Modern UI

```
View → ViewModel → Application service → Repository/provider/storage
```

No network/persistence/shuffle logic in code-behind beyond a narrow UI
reason; keep code-behind small. Use Playnite/theme resources for
backgrounds, foregrounds, borders, accents, buttons, hover/selection/
disabled/focus states — no hard-coded black/white palette. Test both
themes for any important UI state.

SteamGridDB browser flow: open from a game (title prefilled) → search →
resolve ambiguous game → browse covers → single click selects → selection
is visibly obvious → Add Selected Cover. Cards should support normal/hover/
pressed/selected/focused/disabled-already-added states with accessible
names. Never make an image look clickable while routing the click
elsewhere.

Aim for modern-extension feel: clear hierarchy, restrained spacing,
consistent cards, responsive grids, clear primary actions, meaningful empty
states, loading indicators, clear errors, obvious selection, keyboard nav.
Avoid: nested borders, dead whitespace, hard-coded colors, unexplained
icons, tiny click targets, fake-interactive controls, needless modals.

Represent async states explicitly: idle/loading/success/empty/error/
disabled/selected. Never leave the user guessing if a request is running;
prevent duplicate-click search/downloads; never freeze the Playnite UI
during image/network loads.

## 31–34. Menu, Manager, New-Game Automation, Notifications

- **Game context menu**: Enable/Disable Cover Shuffle, Add Cover, Shuffle
  Now, Manage Covers, Restore Original Cover — grouped, state-aware. No
  duplicate items after reload; no settings exposed for unimplemented
  features.
- **Manager**: already supports searchable management, bulk ops, import/
  export, maintenance — don't replace without a demonstrated problem.
  Improvements should track: enabled state, cover count, current cover,
  next shuffle, effective interval, search/filter, safe bulk actions,
  empty states, clear destructive-action confirmation. Bulk actions reuse
  the same services/policies as single-game actions.
- **New-game automation**: Do Nothing / Ask / Automatic. Keep Automatic
  lightweight — don't silently query SteamGridDB for every new game;
  provider/network-heavy automation stays explicit. A game shouldn't
  re-trigger new-game behavior once already seen.
- **Notifications**: useful, not noisy; preserve existing preferences; no
  success notification per internal op unless opted in; user-facing errors
  stay concise while raw exceptions go to the log only.

## 35–36. Error Handling & Logging

Pattern: Detect → log useful technical context → recover or fail safely →
concise user-facing message when useful. Never expose stack traces,
credentials, or implementation details to users. Must degrade gracefully
for: provider unavailable, invalid API key, download failure, deleted
cover file, malformed JSON, invalid game ID, corrupt image, invalid
settings, interrupted import, missing dependency.

Use existing logging conventions (Debug/Information/Warning/Error) with
enough context for scheduling, provider, storage, persistence, integration,
and import/export issues. Never log secrets or unnecessary sensitive data.
Don't log every routine shuffle at a noisy level by default.

## 37–38. Testing

Tests are first-class and must stay independent of a real Playnite
install, a live SteamGridDB key, internet access, or user-specific
filesystem state — use fakes/mocks for providers and time/randomness.

High-value coverage: **Shuffle** (0/1/multi covers, no-repeat, cycle
exhaustion/reshuffle, disabled/missing covers, persisted state, pool
changes). **Scheduling** (due/not due, startup handling, missed intervals,
interval changes, disabled games, missing covers, persisted timestamps).
**Configuration** (defaults, overrides, validation, reset/inheritance).
**Persistence** (save/load, corrupt data, schema version, migration,
round-trip). **Providers** (normalization, auth/network failure, malformed
response, duplicates, source ID). **Cover management** (import, validation,
limit enforcement, dedupe, restoration, missing files). **UI/ViewModels**
(search/selection/add-button/loading/error/empty states, commands).

Test observable behavior and invariants, not implementation mirrors.

Unit tests can't catch every WPF interaction bug — when changing
interactive controls, manually verify click, keyboard focus, selection,
hover, enabled/disabled, dark/light theme, resizing, scrolling. If
automated UI testing is impractical, document manual steps in the
completion report. (Reminder: a cover once looked clickable but wasn't —
ViewModel tests alone don't catch that class of bug.)

## 39. Performance

Avoid repeated full-library scans when event-driven works, repeated
provider queries/downloads, unnecessary UI-thread image decoding, duplicate
per-game timers, excessive DB rewrites. Don't optimize without a measured
or identified bottleneck. A large library must not make the manager/
settings unusably slow.

## 40. Live Scheduler — Future Requirements

One coherent scheduler service; cancellation token / deterministic
shutdown; no UI-thread blocking; no duplicate workers after reload;
persist next-run timestamps; wake/recalculate on relevant setting changes;
handle sleep/restart sensibly; never replay a backlog of every missed
interval; skip invalid/missing covers safely. Prefer one scheduler over one
timer per game.

## 41. Import/Export

Must stay backward compatible. Validate structure, version/schema, game
identity, cover references, image files. Never silently overwrite unrelated
data; report unmatched games clearly. Distinguish future config-export from
full backup/restore; don't claim cross-library remapping unless it's
actually implemented.

## 42. Maintenance & Cleanup

Scanning never deletes. Cleanup must: show what will be affected, require
explicit confirmation, delete only plugin-owned data that's safe to delete,
never delete Playnite's original artwork as routine cleanup. Categories:
orphaned cover files, missing cover records/files, stale provider cache,
unused cache, invalid records. Every new cleanup action must define
ownership before deletion.

## 43. Backup & Recovery

Before major persistence changes, plan how existing users recover their
data. Don't use "we'll add backup" as an excuse for an unnecessary storage-
model change. Any backup/archive format needs enough version metadata for
future recovery.

## 44–46. Dependencies, Packaging, Versioning

Targets `net462`; references PlayniteSDK `6.16.0`, Newtonsoft.Json
`13.0.3`. PlayniteSDK is compile-time only — don't package a runtime copy
Playnite already supplies. Keep `DeployForPlaynite`/`PackageExtension`
targets intact. Dependency version changes require: verify Playnite
compatibility, build, test loading, check packaging contents, document why.

The `.pext` ships only what Playnite needs — no tests, source, SDK runtime
copies, credentials, dev artifacts, debug files. Inspect package contents
before release rather than trusting the MSBuild rule blindly.

Keep project version, assembly version, `extension.yaml`, and README/
release notes aligned; use semver. A change affecting persisted data needs
extra care even for a "minor" bump.

## 47. Security

Never commit/hard-code/log secrets; never trust remote filenames as paths;
never execute downloaded files; never write outside controlled plugin
storage without explicit need; never expose credentials via UI diagnostics.
Treat provider responses as untrusted input — validate lengths,
identifiers, URLs, filenames, image data.

## 48. Documentation Contract

The README describes user-visible behavior; update it in the same phase
when behavior changes materially. Never document aspirational functionality
as implemented. Keep limitations honest; document any required manual
setup step.

## 49–50. Code Style & Comments

`PascalCase` classes/interfaces(`I`-prefixed)/methods, `_camelCase` private
fields, `camelCase` locals. Follow existing repo style where consistent;
avoid unnecessary abbreviations; prefer small focused methods over deep
nesting or clever patterns without concrete benefit.

Comments explain **why**, not what — Playnite API quirks, persistence
compatibility decisions, scheduling edge cases, provider-specific behavior,
security decisions. Don't narrate each line.

## 51. When Requirements Are Ambiguous

Don't silently invent major product behavior. For anything touching
architecture, persistence, user data, or visible behavior: identify the
ambiguity, state your assumption, choose the smallest reversible option if
safe, proceed on low-risk decisions, but ask when the decision risks data
loss or major product divergence. Don't block on trivia.

## 52–53. Don't Over-Engineer / Don't Refactor for Style Alone

No unnecessary DI frameworks, databases, event buses, generic repositories,
one-method abstractions, or third-party UI libraries — keep this small
plugin maintainable with the simplest design that stays testable and
flexible. Don't turn a phase into a repo-wide cleanup; record unrelated
tech debt, fix only if it blocks the current feature or is a clear safety
risk, otherwise defer. Keep diffs reviewable.

## 54. Feature Roadmap (guidance, not a to-do list — implement only the
requested phase)

- **A — Core Shuffle Reliability**: invariants, cycle persistence, disabled/
  missing covers, manual shuffle, state recovery.
- **B — Live Scheduling**: background scheduling, lifecycle, cancellation,
  next-run calc, sleep/restart behavior, no duplicate workers.
- **C — SteamGridDB 2.0**: multi-match UX, correct-game selection, stronger
  matching, filtering, rate-limit handling, caching, load feedback.
- **D — UI/UX Modernisation**: theme compatibility, modern browser, reliable
  click selection, preview, responsive grid, state coverage, a11y, manager
  polish.
- **E — Effective Settings**: true inheritance, per-setting overrides, reset
  to default, effective-settings service, config migration.
- **F — Advanced Cover Management**: per-cover enable/disable, favourites,
  ordering, usage stats, least-used/weighted shuffle, missing-cover
  recovery, drag/drop import.
- **G — Playnite Integration**: better launch-shuffle controls, new-game
  workflow, stronger metadata artwork support, event-driven integration.
- **H — Portability & Recovery**: robust import matching, backup/restore,
  migration framework, recovery tooling.
- **I — Hardening**: race conditions, file locking, reload/shutdown,
  corrupted data, missing files, provider failures, large libraries,
  memory, responsiveness.
- **J — Release Candidate**: CI, build/package verification, clean install,
  upgrade testing, docs, release notes, final polish.

## 55. High-Value Future Features (not permission to build them now)

Cover preview · per-cover enable/disable · favourites · usage info ·
least-used shuffle · missing-cover redownload · drag-and-drop import ·
seasonal cover groups (once scheduler/model are stable) · safe diagnostics
summary (plugin/version/DB/provider status, no secrets).

## 56. Requires Explicit Approval Before Adding

AI-generated artwork, a separate background/tray app, a new database
technology, a custom Playnite theme dependency, an additional online
provider, telemetry/analytics, account systems, cloud sync, automatic
network scraping.

## 57. Development Workflow

**Before coding**: read this file, inspect the repo tree, read relevant
implementation + tests + README section, identify what exists, define the
smallest coherent change.
**During**: preserve architecture, reuse existing services/abstractions,
scope tightly, add tests with the implementation, avoid unrelated
formatting churn, check error handling/persistence.
**After**: build, run tests, inspect the diff, verify no secrets/dev files
leaked, verify no future-phase feature slipped in, update docs if needed,
write the completion report.

## 58. Required Self-Review Before Final Response

```
[ ] Inspected existing implementation first?
[ ] Preserved working functionality?
[ ] Worked only within requested scope?
[ ] Preserved user data and schema compatibility?
[ ] Handled errors?
[ ] Added appropriate tests?
[ ] Built the project and ran the relevant tests?
[ ] Checked dark/light UI and Playnite lifecycle where applicable?
[ ] Inspected the final diff, confirmed no secrets introduced?
[ ] Documented limitations honestly, with manual Playnite test steps?
```

If any answer is no, say so explicitly — don't call the phase fully
complete.

## 59. Final Rule

Optimize for a plugin users can trust: safe artwork management, reliable
randomization, predictable scheduling, excellent Playnite integration,
polished UI, offline-capable covers, robust persistence, clear errors,
maintainable architecture, useful tests, easy upgrades.

**Inspect first. Change only what is necessary. Test everything important.
Protect user data. Never claim functionality that does not actually work.**
