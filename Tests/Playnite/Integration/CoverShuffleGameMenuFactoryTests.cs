using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Domain.Providers;
using PluginCoverShuffle.Domain.Shuffling;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Services;
using PluginCoverShuffle.Tests.Fakes;
using Xunit;

namespace PluginCoverShuffle.Tests.Playnite.Integration
{
    public class CoverShuffleGameMenuFactoryTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverStorage _storage;
        private readonly FakePlayniteGameService _gameService;
        private readonly PlayniteCoverService _coverService;
        private readonly CoverImportService _importService;
        private readonly FakeCoverProvider _steamGridDbProvider;
        private readonly FakeCoverProvider _playniteMetadataProvider;
        private readonly FakeDialogsFactory _dialogs;
        private readonly CoverShuffleGameMenuFactory _factory;

        public CoverShuffleGameMenuFactoryTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CoverShuffleMenuFactoryTests_" + Guid.NewGuid().ToString("N"));
            var databaseFilePath = Path.Combine(_tempDirectory, "coverShuffle.db.json");
            _repository = new CoverShuffleRepository(databaseFilePath);
            var layout = new CoverStorageLayout(Path.Combine(_tempDirectory, "storage"));
            layout.EnsureDirectoriesExist();
            _storage = new CoverStorage(layout);
            _gameService = new FakePlayniteGameService();
            _coverService = new PlayniteCoverService(_repository, _gameService, _storage, () => new CoverShuffleSettings(), new FakeCoverShuffleLogger(), new ShuffleEngine(new FakeShuffleRandomizer()));
            _importService = new CoverImportService(_repository, _storage, new FakeCoverShuffleLogger(), new ImageNormalizationService());
            _steamGridDbProvider = new FakeCoverProvider { Source = CoverSource.SteamGridDb };
            _playniteMetadataProvider = new FakeCoverProvider { Source = CoverSource.PlayniteMetadata };
            _dialogs = new FakeDialogsFactory();
            _factory = new CoverShuffleGameMenuFactory(
                _coverService, _importService, _steamGridDbProvider, _playniteMetadataProvider, _repository, _storage, _dialogs, new FakeCoverShuffleLogger(), _gameService);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        private static Game NewGame() => new Game { Id = Guid.NewGuid() };

        [Fact]
        public void BuildMenuItems_ForDisabledGame_OffersEnableNotDisable()
        {
            var game = NewGame();

            var items = _factory.BuildMenuItems(new List<Game> { game }).ToList();

            Assert.Contains(items, i => i.Description == "Enable Cover Shuffle");
            Assert.DoesNotContain(items, i => i.Description == "Disable Cover Shuffle");
        }

        [Fact]
        public void BuildMenuItems_ForEnabledGame_OffersDisableNotEnable()
        {
            var game = NewGame();
            _coverService.EnableCoverShuffle(game.Id);

            var items = _factory.BuildMenuItems(new List<Game> { game }).ToList();

            Assert.Contains(items, i => i.Description == "Disable Cover Shuffle");
            Assert.DoesNotContain(items, i => i.Description == "Enable Cover Shuffle");
        }

        [Fact]
        public void BuildMenuItems_RestoreOriginalCover_OnlyOfferedWhenOriginalIsSaved()
        {
            var game = NewGame();

            var beforeEnable = _factory.BuildMenuItems(new List<Game> { game }).ToList();
            Assert.DoesNotContain(beforeEnable, i => i.Description == "Restore Original Cover");

            _coverService.EnableCoverShuffle(game.Id);

            var afterEnable = _factory.BuildMenuItems(new List<Game> { game }).ToList();
            Assert.Contains(afterEnable, i => i.Description == "Restore Original Cover");
        }

        [Fact]
        public void EnableMenuItem_Action_EnablesCoverShuffleForTheGame()
        {
            var game = NewGame();
            var items = _factory.BuildMenuItems(new List<Game> { game }).ToList();
            var enableItem = items.Single(i => i.Description == "Enable Cover Shuffle");

            enableItem.Action(new GameMenuItemActionArgs { Games = new List<Game> { game } });

            Assert.True(_coverService.IsEnabled(game.Id));
        }

        [Fact]
        public void RestoreMenuItem_Action_RestoresOriginalCoverForTheGame()
        {
            var game = NewGame();
            _gameService.SeedCoverReference(game.Id, "original.png");
            _coverService.EnableCoverShuffle(game.Id);
            _gameService.SetCoverReference(game.Id, "shuffled.png");

            var items = _factory.BuildMenuItems(new List<Game> { game }).ToList();
            var restoreItem = items.Single(i => i.Description == "Restore Original Cover");

            restoreItem.Action(new GameMenuItemActionArgs { Games = new List<Game> { game } });

            Assert.Equal("original.png", _gameService.GetCoverReference(game.Id));
        }

        [Fact]
        public void ShuffleNowItem_WithNoCovers_ShowsInformationalMessage()
        {
            var game = NewGame();
            var items = _factory.BuildMenuItems(new List<Game> { game }).ToList();
            var shuffleNow = items.Single(i => i.Description == "Shuffle Now");

            shuffleNow.Action(new GameMenuItemActionArgs { Games = new List<Game> { game } });

            Assert.Single(_dialogs.ShownMessages);
        }

        [Fact]
        public void AddLocalFileItem_Exists_UnderAddCoverSubmenu()
        {
            var items = _factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            var addLocalFile = items.Single(i => i.Description == "Local File");
            Assert.Equal("Cover Shuffle|Add Cover", addLocalFile.MenuSection);
        }

        [Fact]
        public void ManageCoversItem_IsPresentForASingleGame()
        {
            var items = _factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            Assert.Contains(items, i => i.Description == "Manage Covers");
        }

        [Fact]
        public void BuildMenuItems_ForMultipleGames_OmitsItemsThatOpenAWindowPerGame()
        {
            // Regression: selecting N games and choosing Manage Covers or an
            // Add Cover source opened N modal windows back to back.
            var items = _factory.BuildMenuItems(new List<Game> { NewGame(), NewGame(), NewGame() }).ToList();

            Assert.DoesNotContain(items, i => i.Description == "Manage Covers");
            Assert.DoesNotContain(items, i => i.MenuSection.EndsWith("|Add Cover"));
        }

        [Fact]
        public void BuildMenuItems_ForMultipleGames_KeepsActionsThatApplyToEveryGame()
        {
            var items = _factory.BuildMenuItems(new List<Game> { NewGame(), NewGame() }).ToList();

            Assert.Contains(items, i => i.Description == "Enable Cover Shuffle");
            Assert.Contains(items, i => i.Description == "Disable Cover Shuffle");
            Assert.Contains(items, i => i.Description == "Shuffle Now");
            Assert.Contains(items, i => i.Description == "Restore Original Cover");
        }

        [Fact]
        public void SteamGridDbItem_Exists_UnderAddCoverSubmenu_WhenProviderAvailable()
        {
            var items = _factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            var addSteamGridDb = items.Single(i => i.Description == "SteamGridDB");
            Assert.Equal("Cover Shuffle|Add Cover", addSteamGridDb.MenuSection);
        }

        [Fact]
        public void SteamGridDbItem_IsOmitted_WhenProviderUnavailable()
        {
            var factory = new CoverShuffleGameMenuFactory(
                _coverService, _importService, null, _playniteMetadataProvider, _repository, _storage, _dialogs, new FakeCoverShuffleLogger());

            var items = factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            Assert.DoesNotContain(items, i => i.Description == "SteamGridDB");
        }

        [Fact]
        public void PlayniteMetadataItem_Exists_UnderAddCoverSubmenu_WhenProviderAvailable()
        {
            var items = _factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            var addPlayniteMetadata = items.Single(i => i.Description == "Playnite Metadata");
            Assert.Equal("Cover Shuffle|Add Cover", addPlayniteMetadata.MenuSection);
        }

        [Fact]
        public void PlayniteMetadataItem_IsOmitted_WhenProviderUnavailable()
        {
            var factory = new CoverShuffleGameMenuFactory(
                _coverService, _importService, _steamGridDbProvider, null, _repository, _storage, _dialogs, new FakeCoverShuffleLogger());

            var items = factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            Assert.DoesNotContain(items, i => i.Description == "Playnite Metadata");
        }

        [Fact]
        public void BuildMenuItems_WhenCoverServiceUnavailable_ReturnsSingleInformationalItem()
        {
            var factory = new CoverShuffleGameMenuFactory(null, null, null, null, null, null, _dialogs, new FakeCoverShuffleLogger());

            var items = factory.BuildMenuItems(new List<Game> { NewGame() }).ToList();

            Assert.Single(items);
            Assert.Contains("unavailable", items[0].Description, StringComparison.OrdinalIgnoreCase);
        }
    }
}
