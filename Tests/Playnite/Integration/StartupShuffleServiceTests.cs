using System;
using System.IO;
using System.Linq;
using Playnite.SDK;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Domain.Shuffling;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Tests.Fakes;
using Xunit;

namespace PluginCoverShuffle.Tests.Playnite.Integration
{
    public class StartupShuffleServiceTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverStorage _storage;
        private readonly FakePlayniteGameService _gameService;
        private readonly PlayniteCoverService _coverService;
        private readonly FakeNotificationsApi _notifications = new FakeNotificationsApi();
        private CoverShuffleSettings _globalSettings = new CoverShuffleSettings();
        private readonly StartupShuffleService _service;

        public StartupShuffleServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "StartupShuffleServiceTests_" + Guid.NewGuid().ToString("N"));
            var databaseFilePath = Path.Combine(_tempDirectory, "coverShuffle.db.json");
            _repository = new CoverShuffleRepository(databaseFilePath);
            var layout = new CoverStorageLayout(Path.Combine(_tempDirectory, "storage"));
            layout.EnsureDirectoriesExist();
            _storage = new CoverStorage(layout);
            _gameService = new FakePlayniteGameService();
            _coverService = new PlayniteCoverService(_repository, _gameService, _storage, () => _globalSettings, new FakeCoverShuffleLogger(), new ShuffleEngine(new FakeShuffleRandomizer()));
            var scheduledShuffleService = new ScheduledShuffleService(_repository, _coverService, new FakeCoverShuffleLogger(), _gameService);
            _service = new StartupShuffleService(_repository, scheduledShuffleService, new FakeCoverShuffleLogger(), new CoverShuffleNotificationService(_notifications));
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        private Cover AddStoredCover(Guid gameId)
        {
            var sourceFile = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(sourceFile, new byte[] { 1, 2, 3 });
            var coverId = Guid.NewGuid();
            var relativePath = _storage.SaveCoverFile(gameId, coverId, sourceFile);
            var cover = new Cover
            {
                CoverId = coverId,
                GameId = gameId,
                Source = CoverSource.LocalFile,
                LocalPath = relativePath,
                Hash = coverId.ToString("N"),
                AddedAt = DateTime.UtcNow,
                IsEnabled = true
            };
            _repository.AddCover(cover);
            return cover;
        }

        /// <summary>An enabled, installed game with two covers, ready to shuffle.</summary>
        private Guid AddEligibleGame(string name = "Some Game")
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, true);
            _gameService.SeedGameName(gameId, name);
            _coverService.EnableCoverShuffle(gameId);
            return gameId;
        }

        private void OverrideShuffleOnStartup(Guid gameId, bool value)
        {
            var configuration = _repository.GetGameConfiguration(gameId);
            configuration.SettingsOverride.ShuffleOnStartup = value;
            _repository.SaveGameConfiguration(configuration);
        }

        [Fact]
        public void RunStartupShuffles_ShufflesEveryKnownEnabledInstalledGame()
        {
            AddEligibleGame();
            AddEligibleGame();

            _service.RunStartupShuffles();

            Assert.Equal(2, _gameService.SetCoverReferenceCalls.Count);
        }

        [Fact]
        public void RunStartupShuffles_ShufflesEveryStartup_EvenWhenTheIntervalIsNotDue()
        {
            // Startup shuffling means a fresh cover every time Playnite
            // opens, not only once the interval has elapsed.
            var gameId = AddEligibleGame();
            _service.RunStartupShuffles();
            Assert.True(_repository.GetShuffleState(gameId).NextShuffleAt > DateTime.UtcNow);

            _service.RunStartupShuffles();

            Assert.Equal(2, _gameService.SetCoverReferenceCalls.Count);
        }

        [Fact]
        public void RunStartupShuffles_SkipsGamesThatAreNotEnabled()
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, true);
            _coverService.EnableCoverShuffle(gameId);
            _coverService.DisableCoverShuffle(gameId);

            _service.RunStartupShuffles();

            Assert.Empty(_gameService.SetCoverReferenceCalls);
        }

        [Fact]
        public void RunStartupShuffles_SkipsGamesThatAreNotInstalled()
        {
            var gameId = AddEligibleGame();
            _gameService.SeedInstalled(gameId, false);

            _service.RunStartupShuffles();

            Assert.Empty(_gameService.SetCoverReferenceCalls);
        }

        [Fact]
        public void RunStartupShuffles_WhenShuffleOnStartupDisabledGlobally_DoesNothing()
        {
            _globalSettings = new CoverShuffleSettings { ShuffleOnStartup = false };
            AddEligibleGame();

            _service.RunStartupShuffles();

            Assert.Empty(_gameService.SetCoverReferenceCalls);
        }

        [Fact]
        public void RunStartupShuffles_GameOverrideOn_ShufflesEvenWhenGlobalIsOff()
        {
            // Regression: the per-game override used to be ignored because
            // the service returned early on the global setting alone.
            _globalSettings = new CoverShuffleSettings { ShuffleOnStartup = false };
            var gameId = AddEligibleGame();
            OverrideShuffleOnStartup(gameId, true);

            _service.RunStartupShuffles();

            Assert.Single(_gameService.SetCoverReferenceCalls);
        }

        [Fact]
        public void RunStartupShuffles_GameOverrideOff_IsSkippedEvenWhenGlobalIsOn()
        {
            var optedOut = AddEligibleGame();
            var followsGlobal = AddEligibleGame();
            OverrideShuffleOnStartup(optedOut, false);

            _service.RunStartupShuffles();

            Assert.Equal(new[] { followsGlobal }, _gameService.SetCoverReferenceCalls.Select(c => c.GameId));
        }

        [Fact]
        public void RunStartupShuffles_OneGameThrowing_DoesNotStopOthers()
        {
            var brokenGameId = AddEligibleGame();
            var healthyGameId = AddEligibleGame();
            _gameService.GameIdToThrowOn = brokenGameId;

            _service.RunStartupShuffles();

            Assert.Contains(healthyGameId, _gameService.SetCoverReferenceCalls.ConvertAll(c => c.GameId));
        }

        [Fact]
        public void RunStartupShuffles_ManyGames_PostsOneSummaryNotificationNotOnePerGame()
        {
            for (var i = 0; i < 5; i++)
            {
                AddEligibleGame("Game " + i);
            }

            _service.RunStartupShuffles();

            var message = Assert.Single(_notifications.AddedMessages);
            Assert.Equal(NotificationType.Info, message.Type);
            Assert.Contains("5 games", message.Text);
        }

        [Fact]
        public void RunStartupShuffles_RespectsEachGamesNotificationPreference()
        {
            _globalSettings = new CoverShuffleSettings { NotificationPreference = NotificationPreference.NotifyOnErrorOnly };
            AddEligibleGame();

            _service.RunStartupShuffles();

            Assert.Empty(_notifications.AddedMessages);
        }

        [Fact]
        public void RunStartupShuffles_Failures_AreSummarisedInOneErrorNotification()
        {
            for (var i = 0; i < 4; i++)
            {
                // Enabled and installed, but with no covers: the shuffle fails.
                var gameId = Guid.NewGuid();
                _gameService.SeedInstalled(gameId, true);
                _gameService.SeedGameName(gameId, "Empty " + i);
                _coverService.EnableCoverShuffle(gameId);
            }

            _service.RunStartupShuffles();

            var message = Assert.Single(_notifications.AddedMessages);
            Assert.Equal(NotificationType.Error, message.Type);
            Assert.Contains("4 games", message.Text);
            Assert.Contains("and 1 more", message.Text);
        }
    }
}
