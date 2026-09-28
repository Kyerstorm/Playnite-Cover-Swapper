using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PluginCoverShuffle.Domain.Providers;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Services;
using PluginCoverShuffle.UI;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Builds the "Plugin Cover Shuffle" right-click game menu. Playnite calls
    /// <see cref="BuildMenuItems"/> fresh on every menu open, so no state is
    /// cached here and no duplicate entries can accumulate.
    /// </summary>
    public class CoverShuffleGameMenuFactory
    {
        private const string MenuSectionName = "Cover Shuffle";

        private readonly PlayniteCoverService _coverService;
        private readonly CoverImportService _importService;
        private readonly ICoverProvider _steamGridDbProvider;
        private readonly ICoverProvider _playniteMetadataProvider;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverStorage _storage;
        private readonly IDialogsFactory _dialogs;
        private readonly ICoverShuffleLogger _logger;
        private readonly IPlayniteGameService _gameService;
        private readonly BulkConfigurationService _bulkConfigurationService;
        private readonly LocalFileCoverAddService _localFileCoverAddService;

        public CoverShuffleGameMenuFactory(
            PlayniteCoverService coverService,
            CoverImportService importService,
            ICoverProvider steamGridDbProvider,
            ICoverProvider playniteMetadataProvider,
            ICoverShuffleRepository repository,
            ICoverStorage storage,
            IDialogsFactory dialogs,
            ICoverShuffleLogger logger,
            IPlayniteGameService gameService = null,
            BulkConfigurationService bulkConfigurationService = null)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // These may all be null together when plugin storage failed to
            // initialize; menu falls back to a single informational item.
            _coverService = coverService;
            _importService = importService;
            _steamGridDbProvider = steamGridDbProvider;
            _playniteMetadataProvider = playniteMetadataProvider;
            _repository = repository;
            _storage = storage;
            _gameService = gameService;
            _bulkConfigurationService = bulkConfigurationService;
            _localFileCoverAddService = importService != null ? new LocalFileCoverAddService(importService) : null;
        }

        public IEnumerable<GameMenuItem> BuildMenuItems(List<Game> games)
        {
            if (_coverService == null)
            {
                return new[]
                {
                    new GameMenuItem
                    {
                        Description = "Cover Shuffle is unavailable (storage failed to initialize)",
                        MenuSection = MenuSectionName,
                        Action = _ => { }
                    }
                };
            }

            // Visibility reflects state only for a single selected game; a
            // multi-selection shows every non-window action and applies it
            // per game.
            var singleGameId = games?.Count == 1 ? games[0].Id : (Guid?)null;
            var isEnabled = singleGameId.HasValue && _coverService.IsEnabled(singleGameId.Value);
            var hasSavedOriginal = singleGameId.HasValue && _coverService.HasSavedOriginalCover(singleGameId.Value);

            var items = new List<GameMenuItem>();

            if (!singleGameId.HasValue || !isEnabled)
            {
                items.Add(new GameMenuItem
                {
                    Description = "Enable Cover Shuffle",
                    MenuSection = MenuSectionName,
                    Action = args => ForEachGame(args, id => _coverService.EnableCoverShuffle(id))
                });
            }

            if (!singleGameId.HasValue || isEnabled)
            {
                items.Add(new GameMenuItem
                {
                    Description = "Disable Cover Shuffle",
                    MenuSection = MenuSectionName,
                    Action = args => ForEachGame(args, id => _coverService.DisableCoverShuffle(id))
                });
            }

            // Add Cover and Manage Covers each open a modal window per game,
            // so on a multi-selection they would chain one dialog after
            // another; they are only offered for a single selected game.
            if (singleGameId.HasValue)
            {
                items.Add(new GameMenuItem
                {
                    Description = "Local File",
                    MenuSection = MenuSectionName + "|Add Cover",
                    Action = args => ForEachGame(args, AddLocalFileCover)
                });

                if (_steamGridDbProvider != null)
                {
                    items.Add(new GameMenuItem
                    {
                        Description = "SteamGridDB",
                        MenuSection = MenuSectionName + "|Add Cover",
                        Action = args => ForEachGame(args, id =>
                        {
                            var gameName = _gameService?.GetGameName(id);
                            var viewModel = new SteamGridDbSearchViewModel(id, gameName, _steamGridDbProvider, _importService, _repository, _logger);
                            SteamGridDbSearchView.ShowDialog(_dialogs, viewModel);
                        })
                    });
                }

                if (_playniteMetadataProvider != null)
                {
                    items.Add(new GameMenuItem
                    {
                        Description = "Playnite Metadata",
                        MenuSection = MenuSectionName + "|Add Cover",
                        Action = args => ForEachGame(args, id =>
                        {
                            var gameName = _gameService?.GetGameName(id);
                            var viewModel = new PlayniteMetadataCoverViewModel(id, gameName, _playniteMetadataProvider, _importService, _repository, _logger);
                            new PlayniteMetadataCoverWindow(viewModel).ShowDialog();
                        })
                    });
                }
            }

            items.Add(new GameMenuItem
            {
                Description = "Shuffle Now",
                MenuSection = MenuSectionName,
                Action = args =>
                {
                    var failures = new List<string>();
                    ForEachGame(args, id =>
                    {
                        var result = _coverService.ShuffleToNextCover(id);
                        if (!result.Success)
                        {
                            failures.Add(result.Message);
                        }
                    });

                    if (failures.Count > 0)
                    {
                        _dialogs.ShowMessage(string.Join(Environment.NewLine, failures.Distinct()));
                    }
                }
            });

            if (singleGameId.HasValue)
            {
                items.Add(new GameMenuItem
                {
                    Description = "Manage Covers",
                    MenuSection = MenuSectionName,
                    Action = args => ForEachGame(args, id =>
                    {
                        var gameName = _gameService?.GetGameName(id);
                        var viewModel = new CoverManagementViewModel(id, gameName, _repository, _storage, _coverService, _bulkConfigurationService);
                        ManageCoversWindow.ShowDialog(
                            _dialogs,
                            viewModel,
                            _repository,
                            _steamGridDbProvider,
                            _playniteMetadataProvider,
                            _importService,
                            _localFileCoverAddService,
                            _logger);
                    })
                });
            }

            if (!singleGameId.HasValue || hasSavedOriginal)
            {
                items.Add(new GameMenuItem
                {
                    Description = "Restore Original Cover",
                    MenuSection = MenuSectionName,
                    Action = args => ForEachGame(args, id => _coverService.RestoreOriginalCover(id))
                });
            }

            return items;
        }

        private void AddLocalFileCover(Guid gameId)
        {
            var viewModel = new AddLocalCoversViewModel(gameId, _repository, _localFileCoverAddService, _logger);
            AddLocalCoversWindow.ShowDialog(_dialogs, viewModel, owner: null);
        }

        private void ForEachGame(GameMenuItemActionArgs args, Action<Guid> action)
        {
            foreach (var game in args.Games ?? Enumerable.Empty<Game>().ToList())
            {
                try
                {
                    action(game.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Cover Shuffle menu action failed for game '{game.Id}'.");
                }
            }
        }
    }
}
