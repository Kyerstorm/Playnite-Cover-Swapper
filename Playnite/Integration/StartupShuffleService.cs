using System;
using System.Collections.Generic;
using System.Linq;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Shuffles every eligible game Cover Shuffle knows about when Playnite
    /// starts - every startup, not only when a game's interval is due (see
    /// <see cref="ScheduledShuffleService.ShuffleForStartup"/>). Only reads
    /// games with a saved <see cref="GameConfiguration"/> from the plugin's
    /// own repository - never scans Playnite's full library - and is meant to
    /// be invoked off the UI thread so it never delays Playnite's startup.
    /// </summary>
    public class StartupShuffleService
    {
        private readonly ICoverShuffleRepository _repository;
        private readonly ScheduledShuffleService _scheduledShuffleService;
        private readonly ICoverShuffleLogger _logger;
        private readonly CoverShuffleNotificationService _notificationService;

        public StartupShuffleService(
            ICoverShuffleRepository repository,
            ScheduledShuffleService scheduledShuffleService,
            ICoverShuffleLogger logger,
            CoverShuffleNotificationService notificationService = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _scheduledShuffleService = scheduledShuffleService ?? throw new ArgumentNullException(nameof(scheduledShuffleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService;
        }

        public void RunStartupShuffles()
        {
            // No global early-exit on ShuffleOnStartup: it is resolved per
            // game, so a game that overrides it on still shuffles when the
            // global default is off, and vice versa.
            var configurations = _repository.GetAllGameConfigurations();
            _logger.Debug($"Running the startup shuffle across {configurations.Count} known game(s).");

            var shuffledToNotify = new List<string>();
            var failuresToNotify = new List<ShuffleAttempt>();

            foreach (var configuration in configurations)
            {
                try
                {
                    var attempt = _scheduledShuffleService.ShuffleForStartup(configuration.GameId);

                    if (attempt.Outcome == ShuffleAttemptOutcome.Shuffled && attempt.Preference == NotificationPreference.NotifyOnShuffle)
                    {
                        shuffledToNotify.Add(attempt.GameName);
                    }
                    else if (attempt.Outcome == ShuffleAttemptOutcome.Failed && attempt.Preference != NotificationPreference.Silent)
                    {
                        failuresToNotify.Add(attempt);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Startup shuffle failed for game '{configuration.GameId}'.");
                }
            }

            _notificationService?.NotifyStartupShuffleSummary(
                shuffledToNotify,
                failuresToNotify.Select(f => new KeyValuePair<string, string>(f.GameName, f.Message)).ToList());
        }
    }
}
