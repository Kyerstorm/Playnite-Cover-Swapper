using System;
using System.IO;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Tests.Fakes;
using Xunit;

namespace PluginCoverShuffle.Tests.Playnite.Integration
{
    public class UninstalledGameScanServiceTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverStorage _storage;
        private readonly FakePlayniteGameService _gameService = new FakePlayniteGameService();
        private readonly UninstalledGameScanService _service;

        public UninstalledGameScanServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UninstalledGameScanServiceTests_" + Guid.NewGuid().ToString("N"));
            var databaseFilePath = Path.Combine(_tempDirectory, "coverShuffle.db.json");
            _repository = new CoverShuffleRepository(databaseFilePath);
            var layout = new CoverStorageLayout(Path.Combine(_tempDirectory, "storage"));
            layout.EnsureDirectoriesExist();
            _storage = new CoverStorage(layout);
            _service = new UninstalledGameScanService(_repository, _gameService, new FakeCoverShuffleLogger());
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

        [Fact]
        public void Scan_WithInstalledGameHavingCovers_ReturnsNoCandidates()
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, true);

            var candidates = _service.Scan();

            Assert.Empty(candidates);
        }

        [Fact]
        public void Scan_WithUninstalledGameHavingCovers_ReturnsOneCandidate()
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, false);
            _gameService.SeedGameName(gameId, "Some Game");

            var candidates = _service.Scan();

            var candidate = Assert.Single(candidates);
            Assert.Equal(gameId, candidate.GameId);
            Assert.Equal("Some Game", candidate.GameName);
        }

        [Fact]
        public void Scan_ReturnsTheCorrectCoverCount()
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, false);

            var candidates = _service.Scan();

            Assert.Equal(2, Assert.Single(candidates).CoverCount);
        }

        [Fact]
        public void Scan_ResolvesGameNameFromPlaynite()
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, false);
            _gameService.SeedGameName(gameId, "Fallout 4");

            var candidates = _service.Scan();

            Assert.Equal("Fallout 4", Assert.Single(candidates).GameName);
        }

        [Fact]
        public void Scan_WithGameNoLongerInPlaynite_StillReturnsACandidate()
        {
            var gameId = Guid.NewGuid();
            AddStoredCover(gameId);
            _gameService.SeedInstalled(gameId, false);
            // No SeedGameName call - GetGameName returns null, matching "game not found".

            var candidates = _service.Scan();

            Assert.Equal("(game not found in Playnite)", Assert.Single(candidates).GameName);
        }

        [Fact]
        public void Scan_WithNoManagedGames_ReturnsEmpty()
        {
            var candidates = _service.Scan();

            Assert.Empty(candidates);
        }

        [Fact]
        public void Scan_WhenGameServiceThrowsForOneGame_StillReturnsCandidatesForTheOthers()
        {
            var throwingGameId = Guid.NewGuid();
            var okGameId = Guid.NewGuid();
            AddStoredCover(throwingGameId);
            AddStoredCover(okGameId);
            _gameService.SeedInstalled(okGameId, false);
            _gameService.GameIdToThrowOnIsGameInstalled = throwingGameId;

            var candidates = _service.Scan();

            var candidate = Assert.Single(candidates);
            Assert.Equal(okGameId, candidate.GameId);
        }
    }
}
