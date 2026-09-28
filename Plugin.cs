using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Domain.Providers;
using PluginCoverShuffle.Domain.Shuffling;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Providers;
using PluginCoverShuffle.Infrastructure.Providers.SteamGridDb;
using PluginCoverShuffle.Infrastructure.Randomization;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Services;
using PluginCoverShuffle.Settings;
using PluginCoverShuffle.UI;

namespace PluginCoverShuffle
{
    /// <summary>
    /// Plugin entry point. Responsible only for Playnite lifecycle wiring;
    /// it must not contain cover, shuffle, or provider logic itself.
    /// </summary>
    public class CoverShufflePlugin : GenericPlugin
    {
        /// <summary>
        /// Stable plugin identifier. Must match the Id in extension.yaml and
        /// must never change once published.
        /// </summary>
        public static readonly Guid PluginId = Guid.Parse("2f6a9b6e-9c3a-4d5a-8f0c-7b3c1a4e6d21");

        public override Guid Id => PluginId;

        private readonly ICoverShuffleLogger _logger;
        private CoverShufflePluginSettingsViewModel _settingsViewModel;

        /// <summary>Narrow view over Playnite's game database (name/cover lookups) that plugin logic depends on instead of <c>IPlayniteAPI</c> directly.</summary>
        internal IPlayniteGameService GameService { get; private set; }

        /// <summary>Persists game configuration, covers, shuffle state, and restoration info.</summary>
        internal ICoverShuffleRepository Repository { get; private set; }

        /// <summary>Manages physical cover image files in plugin-owned storage.</summary>
        internal ICoverStorage Storage { get; private set; }

        /// <summary>Takes control of and restores game cover artwork through Playnite's API.</summary>
        internal PlayniteCoverService CoverService { get; private set; }

        /// <summary>Validates, stores, and registers newly imported cover images.</summary>
        internal CoverImportService ImportService { get; private set; }

        /// <summary>Searches and downloads cover art from SteamGridDB.</summary>
        internal ICoverProvider SteamGridDbProvider { get; private set; }

        /// <summary>Exposes cover/background/icon artwork Playnite already has for a game.</summary>
        internal ICoverProvider PlayniteMetadataProvider { get; private set; }

        /// <summary>Applies at most one due shuffle per game, without piling up missed ones.</summary>
        internal ScheduledShuffleService ScheduledShuffleService { get; private set; }

        /// <summary>Runs the due-shuffle check across every known game at startup, off the UI thread.</summary>
        internal StartupShuffleService StartupShuffleService { get; private set; }

        /// <summary>Reacts to newly installed games not yet known to Cover Shuffle.</summary>
        internal GameInstallationService GameInstallationService { get; private set; }

        /// <summary>Restores original artwork and removes all Cover Shuffle state/files for a genuinely uninstalled game.</summary>
        internal GameUninstallationService GameUninstallationService { get; private set; }

        /// <summary>Shuffles a game's cover right before it launches, for games with that option enabled.</summary>
        internal GameLaunchShuffleService GameLaunchShuffleService { get; private set; }

        /// <summary>Lists every game Cover Shuffle manages, for the Cover Shuffle Manager window.</summary>
        internal CoverShuffleManager Manager { get; private set; }

        /// <summary>Applies enable/disable/interval changes across many games at once.</summary>
        internal BulkConfigurationService BulkConfigurationService { get; private set; }

        /// <summary>Shuffles many games in one operation for the Manager's "Shuffle Selected" / "Shuffle All Installed Games" actions.</summary>
        internal BulkShuffleService BulkShuffleService { get; private set; }

        /// <summary>Exports/imports configuration and covers as a portable CoverShuffle.json bundle.</summary>
        internal ImportExportService ImportExportService { get; private set; }

        /// <summary>Finds and, on explicit confirmation, cleans up orphaned files, invalid records, and cache data.</summary>
        internal MaintenanceService MaintenanceService { get; private set; }

        /// <summary>Runs a read-only maintenance scan at startup and notifies if any covers have a missing file.</summary>
        internal StartupMaintenanceCheckService StartupMaintenanceCheckService { get; private set; }

        /// <summary>Finds managed games (have at least one stored cover) that Playnite now reports as not installed. Read-only.</summary>
        internal UninstalledGameScanService UninstalledGameScanService { get; private set; }

        /// <summary>Runs a read-only uninstalled-game scan at startup and notifies if any managed games appear uninstalled.</summary>
        internal StartupUninstalledGameCheckService StartupUninstalledGameCheckService { get; private set; }

        // Shared across the plugin's lifetime; SteamGridDbClient never disposes it.
        private readonly HttpClient _httpClient = new HttpClient();

        // Read from background threads; volatile so a save on the UI thread
        // (which clears it) is seen promptly.
        private volatile CoverShuffleSettings _cachedGlobalSettings;

        // One queue for all background shuffle work, so the startup pass and
        // launch shuffles never run concurrently (see SerialBackgroundQueue).
        private readonly SerialBackgroundQueue _backgroundQueue;

        private readonly CoverShuffleGameMenuFactory _menuFactory;

        public CoverShufflePlugin(IPlayniteAPI api) : base(api)
        {
            _logger = new PlayniteLoggerAdapter(LogManager.GetLogger());
            _backgroundQueue = new SerialBackgroundQueue(_logger);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            try
            {
                var layout = new CoverStorageLayout(GetPluginUserDataPath());
                layout.EnsureDirectoriesExist();
                Repository = new CoverShuffleRepository(layout.DatabaseFilePath);
                Storage = new CoverStorage(layout);
                _logger.Info($"Cover Shuffle storage initialized at '{layout.RootPath}'.");

                var gameService = new PlayniteGameService(api);
                GameService = gameService;
                var shuffleEngine = new ShuffleEngine(new SystemRandomShuffleRandomizer());
                CoverService = new PlayniteCoverService(Repository, gameService, Storage, GetGlobalSettings, _logger, shuffleEngine);
                ImportService = new CoverImportService(Repository, Storage, _logger, new ImageNormalizationService(), CoverService);

                var steamGridDbClient = new SteamGridDbClient(_httpClient, GetSteamGridDbApiKey, _logger);
                var steamGridDbCache = new SteamGridDbCache(layout.CachePath);
                SteamGridDbProvider = new SteamGridDbCoverProvider(steamGridDbClient, steamGridDbCache, _logger);

                PlayniteMetadataProvider = new PlayniteMetadataCoverProvider(gameService);

                var notificationService = new CoverShuffleNotificationService(api.Notifications);
                ScheduledShuffleService = new ScheduledShuffleService(Repository, CoverService, _logger, gameService, notificationService);
                StartupShuffleService = new StartupShuffleService(Repository, ScheduledShuffleService, _logger, notificationService);

                var newGameConfigurationService = new NewGameConfigurationService(CoverService, PlayniteMetadataProvider, ImportService, GetGlobalSettings, api.Dialogs, _logger);
                GameInstallationService = new GameInstallationService(Repository, newGameConfigurationService, _logger);
                GameUninstallationService = new GameUninstallationService(Repository, Storage, gameService, _logger);
                GameLaunchShuffleService = new GameLaunchShuffleService(CoverService, _logger, gameService, notificationService);

                Manager = new CoverShuffleManager(Repository, CoverService, gameService, Storage);
                BulkConfigurationService = new BulkConfigurationService(CoverService, _logger);
                BulkShuffleService = new BulkShuffleService(Manager, CoverService, gameService, _logger);
                ImportExportService = new ImportExportService(Repository, Storage, _logger);
                MaintenanceService = new MaintenanceService(Repository, Storage, layout, _logger);
                StartupMaintenanceCheckService = new StartupMaintenanceCheckService(MaintenanceService, notificationService, GetGlobalSettings, _logger);
                UninstalledGameScanService = new UninstalledGameScanService(Repository, gameService, _logger);
                StartupUninstalledGameCheckService = new StartupUninstalledGameCheckService(UninstalledGameScanService, notificationService, GetGlobalSettings, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize Cover Shuffle storage. Persistence features will be unavailable this session.");
            }

            _menuFactory = new CoverShuffleGameMenuFactory(
                CoverService, ImportService, SteamGridDbProvider, PlayniteMetadataProvider, Repository, Storage, api.Dialogs, _logger, GameService, BulkConfigurationService);
        }

        /// <summary>
        /// Cached because effective settings are resolved several times per
        /// game, and reading them used to re-deserialize the settings file
        /// from disk every time. The cache is dropped when settings are saved
        /// (see <see cref="GetSettings"/>) and reloaded on next use. It is
        /// deliberately never the settings view model's own instance, which
        /// holds unsaved edits while the settings page is open.
        /// </summary>
        private CoverShuffleSettings GetGlobalSettings()
        {
            var cached = _cachedGlobalSettings;
            if (cached != null)
            {
                return cached;
            }

            cached = LoadPluginSettings<CoverShuffleSettings>() ?? new CoverShuffleSettings();
            _cachedGlobalSettings = cached;
            return cached;
        }

        private string GetSteamGridDbApiKey() => GetGlobalSettings()?.SteamGridDbApiKey;

        private async Task<SteamGridDbValidationResult> ValidateSteamGridDbApiKeyAsync(string apiKeyToValidate)
        {
            using (var validationHttpClient = new HttpClient())
            {
                var client = new SteamGridDbClient(validationHttpClient, () => apiKeyToValidate, _logger);
                var result = await client.ValidateApiKeyAsync(CancellationToken.None).ConfigureAwait(false);
                return new SteamGridDbValidationResult(result.Success, result.Success ? null : result.ErrorMessage);
            }
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            return _menuFactory.BuildMenuItems(args.Games);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (Manager == null || BulkConfigurationService == null || BulkShuffleService == null || ImportExportService == null || MaintenanceService == null || UninstalledGameScanService == null)
            {
                yield break;
            }

            yield return new MainMenuItem
            {
                Description = "Cover Shuffle Manager...",
                MenuSection = "@Cover Shuffle",
                Action = _ =>
                {
                    try
                    {
                        var viewModel = new CoverShuffleManagerViewModel(Manager, BulkConfigurationService, ImportExportService, PlayniteApi.Dialogs);
                        var localFileCoverAddService = ImportService != null ? new LocalFileCoverAddService(ImportService) : null;
                        CoverShuffleManagerWindow.ShowDialog(
                            PlayniteApi.Dialogs,
                            viewModel,
                            MaintenanceService,
                            Repository,
                            Storage,
                            CoverService,
                            BulkConfigurationService,
                            BulkShuffleService,
                            SteamGridDbProvider,
                            PlayniteMetadataProvider,
                            ImportService,
                            localFileCoverAddService,
                            GameService,
                            UninstalledGameScanService,
                            GameUninstallationService,
                            _logger);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to open the Cover Shuffle Manager.");
                        PlayniteApi.Dialogs.ShowMessage("Could not open the Cover Shuffle Manager. See the log for details.");
                    }
                }
            };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            _logger.Info("Cover Shuffle plugin started.");

            if (StartupShuffleService == null)
            {
                return;
            }

            // Runs off the UI thread so a slow disk or a large number of
            // configured games never delays Playnite's own startup.
            _backgroundQueue.Enqueue("Startup checks", () =>
            {
                try
                {
                    // One buffered update: Playnite refreshes its UI once for
                    // the whole pass instead of once per shuffled game.
                    using (PlayniteApi.Database.BufferedUpdate())
                    {
                        StartupShuffleService.RunStartupShuffles();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Startup shuffle check failed unexpectedly.");
                }

                try
                {
                    StartupMaintenanceCheckService?.RunStartupCheck();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Startup maintenance check failed unexpectedly.");
                }

                try
                {
                    StartupUninstalledGameCheckService?.RunStartupCheck();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Startup uninstalled-game check failed unexpectedly.");
                }
            });
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _logger.Info("Cover Shuffle plugin stopped.");
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            if (GameInstallationService == null || args?.Game == null)
            {
                return;
            }

            GameInstallationService.HandleGameInstalled(args.Game.Id, args.Game.Name);
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            if (GameUninstallationService == null || args?.Game == null)
            {
                return;
            }

            GameUninstallationService.HandleGameUninstalled(args.Game.Id, args.Game.Name);
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            if (GameLaunchShuffleService == null || args?.Game == null)
            {
                return;
            }

            var gameId = args.Game.Id;

            // Off the UI thread: this fires synchronously as part of
            // launching the game, and a shuffle is pure file/JSON I/O that
            // must never be what makes "Play" feel slow. Queued behind any
            // startup pass still running rather than racing it.
            _backgroundQueue.Enqueue("Launch shuffle", () => GameLaunchShuffleService.HandleGameStarting(gameId));
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            if (_settingsViewModel == null)
            {
                _settingsViewModel = new CoverShufflePluginSettingsViewModel(
                    this, _logger, ValidateSteamGridDbApiKeyAsync, onSaved: () => _cachedGlobalSettings = null);
            }

            return _settingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new CoverShuffleSettingsView();
        }
    }
}
