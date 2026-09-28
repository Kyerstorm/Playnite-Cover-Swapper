using System;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Infrastructure.Logging;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Runs a read-only uninstalled-game scan once when Playnite starts, so
    /// users find out about managed games that appear uninstalled even if
    /// they never open the Maintenance tool manually. Mirrors
    /// <see cref="StartupMaintenanceCheckService"/>'s one-shot, startup-only
    /// pattern; never deletes anything itself (see
    /// <see cref="UninstalledGameScanService"/>).
    /// </summary>
    public class StartupUninstalledGameCheckService
    {
        private readonly UninstalledGameScanService _scanService;
        private readonly CoverShuffleNotificationService _notificationService;
        private readonly Func<CoverShuffleSettings> _globalSettingsProvider;
        private readonly ICoverShuffleLogger _logger;

        public StartupUninstalledGameCheckService(
            UninstalledGameScanService scanService,
            CoverShuffleNotificationService notificationService,
            Func<CoverShuffleSettings> globalSettingsProvider,
            ICoverShuffleLogger logger)
        {
            _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _globalSettingsProvider = globalSettingsProvider ?? throw new ArgumentNullException(nameof(globalSettingsProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void RunStartupCheck()
        {
            var candidates = _scanService.Scan();
            if (candidates.Count == 0)
            {
                _logger.Debug("Startup uninstalled-game check found no managed games that appear uninstalled.");
                return;
            }

            _logger.Warning($"Startup check found {candidates.Count} managed game(s) that appear uninstalled.");

            var preference = _globalSettingsProvider()?.NotificationPreference ?? NotificationPreference.NotifyOnShuffle;
            _notificationService.NotifyUninstalledGamesWithCovers(candidates.Count, preference);
        }
    }
}
