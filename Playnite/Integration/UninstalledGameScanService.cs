using System;
using System.Collections.Generic;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Read-only scan across every game Cover Shuffle manages (has at least
    /// one stored cover), flagging ones Playnite now reports as not
    /// installed. Never deletes or modifies anything - see
    /// <see cref="GameUninstallationService"/>'s class doc comment for why a
    /// periodic install-state poll must never delete on its own; the caller
    /// (<see cref="StartupUninstalledGameCheckService"/> and the Maintenance
    /// window) is responsible for surfacing candidates for explicit user
    /// confirmation before anything is cleaned up.
    /// </summary>
    public class UninstalledGameScanService
    {
        private readonly ICoverShuffleRepository _repository;
        private readonly IPlayniteGameService _gameService;
        private readonly ICoverShuffleLogger _logger;

        public UninstalledGameScanService(ICoverShuffleRepository repository, IPlayniteGameService gameService, ICoverShuffleLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IReadOnlyList<UninstalledGameCandidate> Scan()
        {
            var candidates = new List<UninstalledGameCandidate>();
            foreach (var gameId in _repository.GetGameIdsWithCovers())
            {
                try
                {
                    if (_gameService.IsGameInstalled(gameId))
                    {
                        continue;
                    }

                    candidates.Add(new UninstalledGameCandidate
                    {
                        GameId = gameId,
                        GameName = _gameService.GetGameName(gameId) ?? "(game not found in Playnite)",
                        CoverCount = _repository.GetCovers(gameId).Count
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, $"Could not determine install state for game '{gameId}' during the uninstalled-game scan.");
                }
            }

            return candidates;
        }
    }
}
