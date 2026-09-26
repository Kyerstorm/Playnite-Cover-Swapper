using System;
using System.IO;
using System.Linq;
using System.Windows;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Services;
using PluginCoverShuffle.Tests.Fakes;
using PluginCoverShuffle.UI;
using Xunit;

namespace PluginCoverShuffle.Tests.UI
{
    public class MaintenanceViewModelTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverStorage _storage;
        private readonly CoverStorageLayout _layout;
        private readonly MaintenanceService _service;
        private readonly FakeDialogsFactory _dialogs = new FakeDialogsFactory();
        private readonly FakePlayniteGameService _gameService = new FakePlayniteGameService();
        private readonly UninstalledGameScanService _uninstalledGameScanService;
        private readonly GameUninstallationService _gameUninstallationService;

        public MaintenanceViewModelTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MaintenanceViewModelTests_" + Guid.NewGuid().ToString("N"));
            var databaseFilePath = Path.Combine(_tempDirectory, "coverShuffle.db.json");
            _repository = new CoverShuffleRepository(databaseFilePath);
            _layout = new CoverStorageLayout(Path.Combine(_tempDirectory, "storage"));
            _layout.EnsureDirectoriesExist();
            _storage = new CoverStorage(_layout);
            _service = new MaintenanceService(_repository, _storage, _layout, new FakeCoverShuffleLogger());
            _uninstalledGameScanService = new UninstalledGameScanService(_repository, _gameService, new FakeCoverShuffleLogger());
            _gameUninstallationService = new GameUninstallationService(_repository, _storage, _gameService, new FakeCoverShuffleLogger());
        }

        private MaintenanceViewModel CreateViewModel()
        {
            return new MaintenanceViewModel(_service, _dialogs, _gameService, _uninstalledGameScanService, _gameUninstallationService);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        private string CreateOrphanFile(Guid? gameId = null)
        {
            var directory = Path.Combine(_layout.CoversPath, (gameId ?? Guid.NewGuid()).ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "orphan.png");
            File.WriteAllBytes(path, new byte[] { 1 });
            return path;
        }

        private Cover AddStoredCoverWithMissingFile(Guid gameId)
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
            File.Delete(_storage.GetAbsolutePath(cover.LocalPath));
            return cover;
        }

        [Fact]
        public void Constructor_ScansImmediately()
        {
            var orphanPath = CreateOrphanFile();

            var viewModel = CreateViewModel();

            Assert.Contains(viewModel.OrphanedFileItems, i => i.FilePath == orphanPath);
            Assert.False(viewModel.IsEmpty);
            Assert.True(viewModel.HasIssues);
        }

        [Fact]
        public void MissingCoverItem_ShowsResolvedGameName()
        {
            var gameId = Guid.NewGuid();
            _gameService.SeedGameName(gameId, "Fallout 4");
            AddStoredCoverWithMissingFile(gameId);

            var viewModel = CreateViewModel();

            Assert.Contains(viewModel.MissingCoverItems, i => i.GameName == "Fallout 4");
        }

        [Fact]
        public void DeleteSelectedOrphanedFiles_WithNothingSelected_DoesNothingAndDoesNotPrompt()
        {
            var orphanPath = CreateOrphanFile();
            var viewModel = CreateViewModel();

            viewModel.DeleteSelectedOrphanedFiles();

            Assert.True(File.Exists(orphanPath));
            Assert.Empty(_dialogs.ShownMessages);
        }

        [Fact]
        public void DeleteSelectedOrphanedFiles_WhenUserDeclines_LeavesFilesInPlace()
        {
            var orphanPath = CreateOrphanFile();
            var viewModel = CreateViewModel();
            viewModel.OrphanedFileItems.Single().IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.No;

            viewModel.DeleteSelectedOrphanedFiles();

            Assert.True(File.Exists(orphanPath));
        }

        [Fact]
        public void DeleteSelectedOrphanedFiles_WhenUserConfirms_DeletesOnlySelectedFiles()
        {
            var keepPath = CreateOrphanFile();
            var deletePath = CreateOrphanFile();
            var viewModel = CreateViewModel();
            viewModel.OrphanedFileItems.Single(i => i.FilePath == deletePath).IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.Yes;

            viewModel.DeleteSelectedOrphanedFiles();

            Assert.True(File.Exists(keepPath));
            Assert.False(File.Exists(deletePath));
            Assert.DoesNotContain(viewModel.OrphanedFileItems, i => i.FilePath == deletePath);
        }

        [Fact]
        public void DeleteSelectedOrphanedFiles_AlwaysAsksForConfirmationFirst()
        {
            CreateOrphanFile();
            var viewModel = CreateViewModel();
            viewModel.OrphanedFileItems.Single().IsSelected = true;

            viewModel.DeleteSelectedOrphanedFiles();

            Assert.Single(_dialogs.ShownMessages);
        }

        [Fact]
        public void RemoveSelectedMissingRecords_OnlyRemovesSelectedRecords()
        {
            var gameId = Guid.NewGuid();
            var keepCover = AddStoredCoverWithMissingFile(gameId);
            var removeCover = AddStoredCoverWithMissingFile(gameId);
            var viewModel = CreateViewModel();
            viewModel.MissingCoverItems.Single(i => i.Cover.CoverId == removeCover.CoverId).IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.Yes;

            viewModel.RemoveSelectedMissingRecords();

            var remaining = _repository.GetCovers(gameId);
            Assert.Contains(remaining, c => c.CoverId == keepCover.CoverId);
            Assert.DoesNotContain(remaining, c => c.CoverId == removeCover.CoverId);
        }

        [Fact]
        public void DeleteSelectedCacheFiles_OnlyDeletesSelectedFiles()
        {
            var keepPath = Path.Combine(_layout.CachePath, "keep.png");
            var deletePath = Path.Combine(_layout.CachePath, "delete.png");
            File.WriteAllBytes(keepPath, new byte[] { 1 });
            File.WriteAllBytes(deletePath, new byte[] { 2 });
            var viewModel = CreateViewModel();
            viewModel.CacheFileItems.Single(i => i.FilePath == deletePath).IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.Yes;

            viewModel.DeleteSelectedCacheFiles();

            Assert.True(File.Exists(keepPath));
            Assert.False(File.Exists(deletePath));
        }

        [Fact]
        public void SelectAllOrphaned_SelectsEveryOrphanedItem()
        {
            CreateOrphanFile();
            CreateOrphanFile();
            var viewModel = CreateViewModel();

            viewModel.SelectAllOrphaned(true);

            Assert.Equal(2, viewModel.SelectedOrphanedCount);

            viewModel.SelectAllOrphaned(false);

            Assert.Equal(0, viewModel.SelectedOrphanedCount);
        }

        [Fact]
        public void SelectedCount_UpdatesWhenAnItemIsToggled()
        {
            CreateOrphanFile();
            var viewModel = CreateViewModel();

            Assert.Equal(0, viewModel.SelectedOrphanedCount);

            viewModel.OrphanedFileItems.Single().IsSelected = true;

            Assert.Equal(1, viewModel.SelectedOrphanedCount);
        }

        [Fact]
        public void IsEmpty_OnCleanState_IsTrue()
        {
            var viewModel = CreateViewModel();

            Assert.True(viewModel.IsEmpty);
            Assert.True(viewModel.IsHealthy);
        }

        [Fact]
        public void Scan_NeverDeletesAnything_ByItself()
        {
            var orphanPath = CreateOrphanFile();
            var viewModel = CreateViewModel();

            viewModel.Scan();
            viewModel.Scan();

            Assert.True(File.Exists(orphanPath));
        }

        [Fact]
        public void Constructor_ScansImmediately_PopulatesUninstalledGameItems()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);

            var viewModel = CreateViewModel();

            Assert.Contains(viewModel.UninstalledGameItems, i => i.GameId == gameId);
        }

        [Fact]
        public void UninstalledGameItem_ShowsResolvedGameName()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);
            _gameService.SeedGameName(gameId, "Fallout 4");

            var viewModel = CreateViewModel();

            Assert.Contains(viewModel.UninstalledGameItems, i => i.GameName == "Fallout 4");
        }

        [Fact]
        public void RestoreAndRemoveSelectedUninstalledGames_WithNothingSelected_DoesNothingAndDoesNotPrompt()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);
            var viewModel = CreateViewModel();

            viewModel.RestoreAndRemoveSelectedUninstalledGames();

            Assert.Empty(_dialogs.ShownMessages);
            Assert.Contains(viewModel.UninstalledGameItems, i => i.GameId == gameId);
        }

        [Fact]
        public void RestoreAndRemoveSelectedUninstalledGames_WhenUserCancels_DoesNotRemoveTheRecord()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);
            var viewModel = CreateViewModel();
            viewModel.UninstalledGameItems.Single().IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.No;

            viewModel.RestoreAndRemoveSelectedUninstalledGames();

            Assert.NotEmpty(_repository.GetCovers(gameId));
        }

        [Fact]
        public void RestoreAndRemoveSelectedUninstalledGames_WhenConfirmed_CallsHandleGameUninstalledForEachSelectedGame()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);
            var viewModel = CreateViewModel();
            viewModel.UninstalledGameItems.Single().IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.Yes;

            viewModel.RestoreAndRemoveSelectedUninstalledGames();

            Assert.Empty(_repository.GetCovers(gameId));
        }

        [Fact]
        public void RestoreAndRemoveSelectedUninstalledGames_AfterSuccess_RemovesTheGameFromTheList()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);
            var viewModel = CreateViewModel();
            viewModel.UninstalledGameItems.Single().IsSelected = true;
            _dialogs.NextMessageBoxResult = MessageBoxResult.Yes;

            viewModel.RestoreAndRemoveSelectedUninstalledGames();

            Assert.DoesNotContain(viewModel.UninstalledGameItems, i => i.GameId == gameId);
        }

        [Fact]
        public void HasIssues_And_IsEmpty_AccountForUninstalledGameItems()
        {
            var gameId = Guid.NewGuid();
            AddStoredCoverWithMissingFile(gameId);
            _gameService.SeedInstalled(gameId, false);

            var viewModel = CreateViewModel();

            Assert.True(viewModel.HasIssues);
            Assert.False(viewModel.IsEmpty);
        }
    }
}
