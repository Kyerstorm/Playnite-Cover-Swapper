using System;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// The core "is a shuffle due, and if so apply it" check for one game.
    /// Reusable from any trigger (startup today; a future in-session timer
    /// or game-launch hook could call the same method).
    /// </summary>
    public class ScheduledShuffleService
    {
        private readonly ICoverShuffleRepository _repository;
        private readonly PlayniteCoverService _coverService;
        private readonly ICoverShuffleLogger _logger;
        private readonly IPlayniteGameService _gameService;
        private readonly CoverShuffleNotificationService _notificationService;

        public ScheduledShuffleService(
            ICoverShuffleRepository repository,
            PlayniteCoverService coverService,
            ICoverShuffleLogger logger,
            IPlayniteGameService gameService = null,
            CoverShuffleNotificationService notificationService = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameService = gameService;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Applies exactly one shuffle if the game is enabled and due, and
        /// always leaves the schedule advanced for next time as part of that
        /// single shuffle. Never performs more than one shuffle per call,
        /// regardless of how long Playnite was closed.
        /// </summary>
        public void ShuffleIfDue(Guid gameId)
        {
            if (!_coverService.IsEnabled(gameId))
            {
                return;
            }

            // Only shuffle games that are actually installed - there's no
            // point rotating artwork for a game the user can't currently
            // play, and a game service isn't always available to check (some
            // callers omit it), so the check is skipped rather than assumed
            // false in that case.
            if (_gameService != null && !_gameService.IsGameInstalled(gameId))
            {
                _logger.Debug($"Scheduled shuffle skipped for game '{gameId}': not installed.");
                return;
            }

            var state = _repository.GetShuffleState(gameId);
            var isDue = state?.NextShuffleAt == null || state.NextShuffleAt.Value <= DateTime.UtcNow;
            if (!isDue)
            {
                return;
            }

            var result = _coverService.ShuffleToNextCover(gameId);
            var gameName = _gameService?.GetGameName(gameId) ?? gameId.ToString();
            var preference = _coverService.GetEffectiveNotificationPreference(gameId);

            if (!result.Success)
            {
                _logger.Debug($"Scheduled shuffle skipped for game '{gameId}': {result.Message}");
                _notificationService?.NotifyShuffleFailed(gameId, gameName, result.Message, preference);
                return;
            }

            _notificationService?.NotifyShuffled(gameId, gameName, preference);
        }

        /// <summary>
        /// The Playnite-startup trigger: shuffles once, every startup, for an
        /// enabled, installed game whose effective "shuffle on startup" is on
        /// (its own override, or the global default). Deliberately ignores
        /// whether the interval is due - startup shuffling means a fresh cover
        /// each time Playnite opens. Never notifies itself: the caller
        /// aggregates every game's result into one notification, so a large
        /// library doesn't produce one notification per game.
        /// </summary>
        public ShuffleAttempt ShuffleForStartup(Guid gameId)
        {
            if (!_coverService.IsEnabled(gameId) || !_coverService.GetEffectiveShuffleOnStartup(gameId))
            {
                return ShuffleAttempt.Skipped;
            }

            if (_gameService != null && !_gameService.IsGameInstalled(gameId))
            {
                _logger.Debug($"Startup shuffle skipped for game '{gameId}': not installed.");
                return ShuffleAttempt.Skipped;
            }

            var result = _coverService.ShuffleToNextCover(gameId);
            var gameName = _gameService?.GetGameName(gameId) ?? gameId.ToString();
            var preference = _coverService.GetEffectiveNotificationPreference(gameId);

            if (!result.Success)
            {
                _logger.Debug($"Startup shuffle skipped for game '{gameId}': {result.Message}");
                return ShuffleAttempt.Failed(gameName, result.Message, preference);
            }

            return ShuffleAttempt.Shuffled(gameName, preference);
        }
    }
}
