using System;
using System.Collections.Generic;
using System.Linq;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Domain.Shuffling;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Services;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Coordinates taking control of and restoring a game's cover artwork.
    /// This is the only place that decides when Cover Shuffle is allowed to
    /// overwrite a game's cover and when the original must be restored.
    /// Also implements <see cref="IInitialShuffleTrigger"/>, the port
    /// <see cref="CoverImportService"/> uses to apply a game's very first
    /// usable cover automatically.
    /// </summary>
    public class PlayniteCoverService : IInitialShuffleTrigger
    {
        private readonly ICoverShuffleRepository _repository;
        private readonly IPlayniteGameService _gameService;
        private readonly ICoverStorage _storage;
        private readonly Func<CoverShuffleSettings> _globalSettingsProvider;
        private readonly ICoverShuffleLogger _logger;
        private readonly IShuffleEngine _shuffleEngine;

        public PlayniteCoverService(
            ICoverShuffleRepository repository,
            IPlayniteGameService gameService,
            ICoverStorage storage,
            Func<CoverShuffleSettings> globalSettingsProvider,
            ICoverShuffleLogger logger,
            IShuffleEngine shuffleEngine)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _globalSettingsProvider = globalSettingsProvider ?? throw new ArgumentNullException(nameof(globalSettingsProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _shuffleEngine = shuffleEngine ?? throw new ArgumentNullException(nameof(shuffleEngine));
        }

        /// <summary>Whether Cover Shuffle is currently enabled for this specific game: its own override, or the current global default.</summary>
        public bool IsEnabled(Guid gameId) => ResolveEffectiveSettings(gameId).Enabled;

        /// <summary>Whether the original cover has been captured and can be restored.</summary>
        public bool HasSavedOriginalCover(Guid gameId)
        {
            return _repository.GetOriginalArtwork(gameId) != null;
        }

        /// <summary>
        /// Takes control of the game's cover: captures the current cover as
        /// the restorable original (only the first time, so a later shuffled
        /// cover is never mistaken for the original) and marks the game as
        /// enabled.
        /// </summary>
        public void EnableCoverShuffle(Guid gameId)
        {
            CaptureOriginalCoverIfMissing(gameId);
            SetEnabled(gameId, true);
            _logger.Info($"Cover Shuffle enabled for game '{gameId}'.");
        }

        /// <summary>
        /// Stops future shuffling for this game. Does not change the
        /// currently displayed cover; use <see cref="RestoreOriginalCover"/>
        /// to put the original artwork back.
        /// </summary>
        public void DisableCoverShuffle(Guid gameId)
        {
            SetEnabled(gameId, false);
            _logger.Info($"Cover Shuffle disabled for game '{gameId}'.");
        }

        /// <summary>
        /// Restores the game's originally captured cover, if one was saved.
        /// Safe to call even if no original was ever captured.
        /// </summary>
        public void RestoreOriginalCover(Guid gameId)
        {
            var original = _repository.GetOriginalArtwork(gameId);
            if (original == null)
            {
                _logger.Warning($"No original cover is saved for game '{gameId}'; nothing to restore.");
                return;
            }

            _gameService.SetCoverReference(gameId, original.OriginalCoverReference);
            _logger.Info($"Restored original cover for game '{gameId}'.");
        }

        /// <summary>
        /// Advances the game's cover to the next cover selected by the
        /// <see cref="IShuffleEngine"/>'s randomized cycle, persisting the
        /// remaining cycle so it survives a restart.
        /// </summary>
        public ShuffleResult ShuffleToNextCover(Guid gameId)
        {
            var covers = _repository.GetCovers(gameId).Where(c => c.IsEnabled).ToList();
            if (covers.Count == 0)
            {
                return ShuffleResult.Failed("This game has no covers configured yet. Use \"Add Cover\" to add one.");
            }

            // Applying a cover overwrites Game.CoverImage, so the true
            // original must be captured first even if Enable was never used.
            CaptureOriginalCoverIfMissing(gameId);

            var state = _repository.GetShuffleState(gameId);
            if (!TryPickNextValidCover(gameId, covers, state, out var selected, out var remainingCycle))
            {
                return ShuffleResult.Failed(
                    "None of this game's covers could be found on disk. Open \"Manage Covers\" or run Maintenance to clean up missing covers.");
            }

            return ApplySelectedCover(gameId, selected, remainingCycle, ShuffleTrigger.Random);
        }

        /// <summary>
        /// Applies the game's very first usable cover automatically, so the
        /// user never has to press "Shuffle Now" just to see their first
        /// added cover take effect. Fires only when the game is enabled, has
        /// at least one valid enabled cover, AND has no persisted
        /// <see cref="Domain.ShuffleState"/> at all - deliberately not a
        /// "cover count == 1" check, which would be unreliable for a game
        /// that already had several covers imported (e.g. via Add All)
        /// before its very first shuffle. Once any shuffle happens (this
        /// one included) a state exists and this never fires again for that
        /// game, so adding a second or third cover afterwards never
        /// auto-changes the current cover.
        /// </summary>
        /// <returns>True if a cover was applied.</returns>
        public bool TryApplyInitialShuffle(Guid gameId)
        {
            if (!IsEnabled(gameId) || _repository.GetShuffleState(gameId) != null)
            {
                return false;
            }

            var covers = _repository.GetCovers(gameId).Where(c => c.IsEnabled).ToList();
            if (covers.Count == 0)
            {
                return false;
            }

            if (!TryPickNextValidCover(gameId, covers, null, out var selected, out var remainingCycle))
            {
                return false;
            }

            // Applying a cover overwrites Game.CoverImage, so the true
            // original must be captured first even if Enable was never used.
            CaptureOriginalCoverIfMissing(gameId);

            ApplySelectedCover(gameId, selected, remainingCycle, ShuffleTrigger.Initial);
            return true;
        }

        /// <summary>See <see cref="IInitialShuffleTrigger"/>.</summary>
        void IInitialShuffleTrigger.TriggerIfNeeded(Guid gameId) => TryApplyInitialShuffle(gameId);

        /// <summary>
        /// Shared "pick the shuffle engine's next cover, skipping any whose
        /// file has gone missing" loop used by both a normal shuffle and the
        /// initial shuffle, so the randomized-selection logic (and the
        /// missing-file skip behaviour) exists in exactly one place.
        /// </summary>
        private bool TryPickNextValidCover(Guid gameId, IReadOnlyList<Cover> covers, ShuffleState startState, out Cover selected, out IReadOnlyList<Guid> remainingCycle)
        {
            var coverIds = covers.Select(c => c.CoverId).ToList();
            var cycleState = startState;

            // A cover's file can vanish outside the plugin (manual deletion,
            // a sync tool, antivirus quarantine). Applying a reference to a
            // missing file would leave Playnite showing a broken image, so
            // skip such covers rather than apply them; each skip still
            // consumes its slot from the cycle so it isn't retried forever.
            for (var attempt = 0; attempt < coverIds.Count; attempt++)
            {
                var advance = _shuffleEngine.GetNext(coverIds, cycleState);
                var candidate = covers.First(c => c.CoverId == advance.SelectedCoverId);

                if (!_storage.CoverFileExists(candidate.LocalPath))
                {
                    _logger.Warning($"Cover '{candidate.CoverId}' for game '{gameId}' is missing its file; skipping it. Run Maintenance to clean up invalid cover records.");
                    cycleState = new ShuffleState
                    {
                        GameId = gameId,
                        CurrentCoverId = startState?.CurrentCoverId,
                        LastShuffleAt = startState?.LastShuffleAt,
                        NextShuffleAt = startState?.NextShuffleAt,
                        ShuffleCycle = advance.RemainingCycle.ToList()
                    };
                    continue;
                }

                selected = candidate;
                remainingCycle = advance.RemainingCycle;
                return true;
            }

            selected = null;
            remainingCycle = null;
            return false;
        }

        /// <summary>
        /// Applies a specific, user-picked cover, overriding the randomized
        /// shuffle cycle. Recorded with <see cref="ShuffleTrigger.Manual"/> so
        /// it can be distinguished from an engine-selected shuffle for future
        /// statistics.
        /// </summary>
        public ShuffleResult ChooseCover(Guid gameId, Guid coverId)
        {
            var covers = _repository.GetCovers(gameId);
            var selected = covers.FirstOrDefault(c => c.CoverId == coverId);
            if (selected == null)
            {
                return ShuffleResult.Failed("That cover is no longer part of this game's cover pool.");
            }

            if (!_storage.CoverFileExists(selected.LocalPath))
            {
                return ShuffleResult.Failed(
                    "That cover's file could not be found on disk. Open \"Manage Covers\" or run Maintenance to clean up missing covers.");
            }

            // Applying a cover overwrites Game.CoverImage, so the true
            // original must be captured first even if Enable was never used.
            CaptureOriginalCoverIfMissing(gameId);

            var state = _repository.GetShuffleState(gameId);
            var remainingCycle = state?.ShuffleCycle ?? new List<Guid>();

            // The manually chosen cover has now been shown, so it must not
            // stay queued in the randomized cycle - otherwise the very next
            // "Shuffle Now" could hand the same cover right back out,
            // violating the no-immediate-repeat invariant.
            var cycleIndex = remainingCycle.IndexOf(coverId);
            if (cycleIndex >= 0)
            {
                remainingCycle = remainingCycle.ToList();
                remainingCycle.RemoveAt(cycleIndex);
            }

            return ApplySelectedCover(gameId, selected, remainingCycle, ShuffleTrigger.Manual);
        }

        /// <summary>
        /// Applies <paramref name="selected"/> as the game's cover and
        /// records every side effect shared by both a randomized shuffle and
        /// a manual override: usage stats, persisted shuffle state, and the
        /// next scheduled shuffle time.
        /// </summary>
        private ShuffleResult ApplySelectedCover(Guid gameId, Cover selected, IReadOnlyList<Guid> remainingCycle, ShuffleTrigger trigger)
        {
            _gameService.SetCoverReference(gameId, _storage.GetAbsolutePath(selected.LocalPath));

            selected.LastUsedAt = DateTime.UtcNow;
            selected.UsageCount += 1;
            _repository.UpdateCover(selected);

            var now = DateTime.UtcNow;
            _repository.SaveShuffleState(new ShuffleState
            {
                GameId = gameId,
                CurrentCoverId = selected.CoverId,
                LastShuffleAt = now,
                NextShuffleAt = now.Add(GetEffectiveInterval(gameId)),
                ShuffleCycle = remainingCycle.ToList(),
                LastShuffleTrigger = trigger
            });

            var verb = trigger == ShuffleTrigger.Manual ? "Manually chose" : trigger == ShuffleTrigger.Initial ? "Applied initial" : "Shuffled to";
            _logger.Info($"{verb} cover '{selected.CoverId}' for game '{gameId}'.");
            return ShuffleResult.Ok();
        }

        /// <summary>The interval currently governing this game's scheduled shuffles: its own override, or the global default.</summary>
        public TimeSpan GetEffectiveInterval(Guid gameId) => ResolveEffectiveSettings(gameId).Interval;

        /// <summary>How much shuffle activity for this game should be surfaced as a Playnite notification: its own override, or the global default.</summary>
        public NotificationPreference GetEffectiveNotificationPreference(Guid gameId) => ResolveEffectiveSettings(gameId).NotificationPreference;

        /// <summary>Whether this game should shuffle its cover each time it launches: its own override, or the global default.</summary>
        public bool GetEffectiveShuffleOnGameLaunch(Guid gameId) => ResolveEffectiveSettings(gameId).ShuffleOnGameLaunch;

        /// <summary>Whether this game should shuffle its cover each time Playnite starts: its own override, or the global default.</summary>
        public bool GetEffectiveShuffleOnStartup(Guid gameId) => ResolveEffectiveSettings(gameId).ShuffleOnStartup;

        /// <summary>
        /// Resolves the effective settings for a game field-by-field: each
        /// setting independently uses the game's own override when present,
        /// or the current global default otherwise. This is what makes
        /// overriding one setting (e.g. interval) not freeze every other
        /// setting away from future global changes.
        /// </summary>
        private CoverShuffleSettings ResolveEffectiveSettings(Guid gameId)
        {
            var global = _globalSettingsProvider() ?? new CoverShuffleSettings();
            var overrides = _repository.GetGameConfiguration(gameId)?.SettingsOverride;

            return new CoverShuffleSettings
            {
                Enabled = overrides?.Enabled ?? global.Enabled,
                Interval = overrides?.Interval ?? global.Interval,
                Mode = overrides?.Mode ?? global.Mode,
                AvoidConsecutiveDuplicates = overrides?.AvoidConsecutiveDuplicates ?? global.AvoidConsecutiveDuplicates,
                ShuffleOnStartup = overrides?.ShuffleOnStartup ?? global.ShuffleOnStartup,
                ShuffleOnGameLaunch = overrides?.ShuffleOnGameLaunch ?? global.ShuffleOnGameLaunch,
                NotificationPreference = overrides?.NotificationPreference ?? global.NotificationPreference,
                NewGameBehavior = overrides?.NewGameBehavior ?? global.NewGameBehavior,
                SteamGridDbApiKey = global.SteamGridDbApiKey
            };
        }

        private void CaptureOriginalCoverIfMissing(Guid gameId)
        {
            if (_repository.GetOriginalArtwork(gameId) != null)
            {
                return;
            }

            var currentCover = _gameService.GetCoverReference(gameId);
            _repository.SaveOriginalArtwork(new OriginalArtworkInfo
            {
                GameId = gameId,
                OriginalCoverReference = currentCover,
                CapturedAtUtc = DateTime.UtcNow
            });
            _logger.Debug($"Captured original cover for game '{gameId}'.");
        }

        /// <summary>Sets a per-game interval override, independent of every other setting.</summary>
        public void SetIntervalOverride(Guid gameId, TimeSpan interval)
        {
            var configuration = GetOrCreateConfiguration(gameId);
            configuration.SettingsOverride.Interval = interval;
            _repository.SaveGameConfiguration(configuration);
        }

        private void SetEnabled(Guid gameId, bool enabled)
        {
            var configuration = GetOrCreateConfiguration(gameId);
            configuration.SettingsOverride.Enabled = enabled;
            _repository.SaveGameConfiguration(configuration);
        }

        /// <summary>
        /// Clears every per-game override for this game so it goes back to
        /// following the global defaults for all settings, live. The
        /// <see cref="GameConfiguration"/> record itself is kept (rather than
        /// deleted) so the game stays known to Cover Shuffle (e.g. still
        /// checked for due shuffles). Safe to call for a game with no
        /// configuration at all.
        /// </summary>
        public void ResetOverridesToGlobalDefaults(Guid gameId)
        {
            var configuration = _repository.GetGameConfiguration(gameId);
            if (configuration == null || configuration.SettingsOverride == null)
            {
                return;
            }

            configuration.SettingsOverride = null;
            _repository.SaveGameConfiguration(configuration);
        }

        /// <summary>
        /// A game's first per-game override starts as an empty (all-inherit)
        /// <see cref="GameSettingsOverride"/> rather than a snapshot of
        /// current global values, so setting one field (e.g. enabling the
        /// game) never freezes any other field away from future global
        /// changes.
        /// </summary>
        private GameConfiguration GetOrCreateConfiguration(Guid gameId)
        {
            var configuration = _repository.GetGameConfiguration(gameId) ?? new GameConfiguration { GameId = gameId };
            if (configuration.SettingsOverride == null)
            {
                configuration.SettingsOverride = new GameSettingsOverride();
            }

            return configuration;
        }
    }
}
