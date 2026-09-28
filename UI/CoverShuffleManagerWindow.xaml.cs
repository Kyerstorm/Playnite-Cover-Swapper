using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using PluginCoverShuffle.Domain.Providers;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Storage;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Services;

namespace PluginCoverShuffle.UI
{
    /// <summary>
    /// The Cover Shuffle Manager: a two-pane window listing every game
    /// Cover Shuffle manages on the left, with the selected game's full
    /// "Manage Covers" control centre embedded on the right. The right pane
    /// reuses <see cref="ManageCoversWindow"/> and <see cref="CoverManagementViewModel"/>
    /// directly (constructing a fresh instance per selected game) rather
    /// than duplicating that per-game logic here. A <see cref="UserControl"/>
    /// rather than a <see cref="Window"/> so it can be hosted inside a
    /// window created by <see cref="IDialogsFactory.CreateWindow"/>, which is
    /// what gives it Playnite's own dark/light theme (see <see cref="ShowDialog"/>)
    /// instead of the plain WPF default a bare Window would fall back to.
    /// </summary>
    public partial class CoverShuffleManagerWindow : UserControl
    {
        private readonly CoverShuffleManagerViewModel _viewModel;
        private readonly MaintenanceService _maintenanceService;
        private readonly IDialogsFactory _dialogs;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverStorage _storage;
        private readonly PlayniteCoverService _coverService;
        private readonly BulkConfigurationService _bulkConfigurationService;
        private readonly BulkShuffleService _bulkShuffleService;
        private readonly ICoverProvider _steamGridDbProvider;
        private readonly ICoverProvider _playniteMetadataProvider;
        private readonly CoverImportService _importService;
        private readonly LocalFileCoverAddService _localFileCoverAddService;
        private readonly IPlayniteGameService _gameService;
        private readonly UninstalledGameScanService _uninstalledGameScanService;
        private readonly GameUninstallationService _gameUninstallationService;
        private readonly ICoverShuffleLogger _logger;

        private CoverManagementViewModel _detailViewModel;
        private Window _owningWindow;

        public CoverShuffleManagerWindow(
            CoverShuffleManagerViewModel viewModel,
            MaintenanceService maintenanceService,
            IDialogsFactory dialogs,
            ICoverShuffleRepository repository,
            ICoverStorage storage,
            PlayniteCoverService coverService,
            BulkConfigurationService bulkConfigurationService,
            BulkShuffleService bulkShuffleService,
            ICoverProvider steamGridDbProvider,
            ICoverProvider playniteMetadataProvider,
            CoverImportService importService,
            LocalFileCoverAddService localFileCoverAddService,
            IPlayniteGameService gameService,
            UninstalledGameScanService uninstalledGameScanService,
            GameUninstallationService gameUninstallationService,
            ICoverShuffleLogger logger)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
            _bulkConfigurationService = bulkConfigurationService ?? throw new ArgumentNullException(nameof(bulkConfigurationService));
            _bulkShuffleService = bulkShuffleService ?? throw new ArgumentNullException(nameof(bulkShuffleService));
            _steamGridDbProvider = steamGridDbProvider;
            _playniteMetadataProvider = playniteMetadataProvider;
            _importService = importService;
            _localFileCoverAddService = localFileCoverAddService;
            _gameService = gameService;
            _uninstalledGameScanService = uninstalledGameScanService ?? throw new ArgumentNullException(nameof(uninstalledGameScanService));
            _gameUninstallationService = gameUninstallationService ?? throw new ArgumentNullException(nameof(gameUninstallationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            DataContext = _viewModel;
        }

        /// <summary>Creates and shows this panel in a Playnite-themed modal window.</summary>
        public static void ShowDialog(
            IDialogsFactory dialogs,
            CoverShuffleManagerViewModel viewModel,
            MaintenanceService maintenanceService,
            ICoverShuffleRepository repository,
            ICoverStorage storage,
            PlayniteCoverService coverService,
            BulkConfigurationService bulkConfigurationService,
            BulkShuffleService bulkShuffleService,
            ICoverProvider steamGridDbProvider,
            ICoverProvider playniteMetadataProvider,
            CoverImportService importService,
            LocalFileCoverAddService localFileCoverAddService,
            IPlayniteGameService gameService,
            UninstalledGameScanService uninstalledGameScanService,
            GameUninstallationService gameUninstallationService,
            ICoverShuffleLogger logger)
        {
            if (dialogs == null)
            {
                throw new ArgumentNullException(nameof(dialogs));
            }

            var view = new CoverShuffleManagerWindow(
                viewModel, maintenanceService, dialogs, repository, storage, coverService, bulkConfigurationService, bulkShuffleService,
                steamGridDbProvider, playniteMetadataProvider, importService, localFileCoverAddService, gameService,
                uninstalledGameScanService, gameUninstallationService, logger);

            var window = dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = true,
                ShowMaximizeButton = true
            });

            window.Title = "Cover Shuffle Manager";
            window.Content = view;
            window.Height = 720;
            window.Width = 1080;
            window.MinHeight = 480;
            window.MinWidth = 760;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            try
            {
                window.Owner = dialogs.GetCurrentAppWindow();
            }
            catch (NotImplementedException)
            {
                // Some IDialogsFactory test doubles don't implement this; the
                // window still works standalone, just without an owner.
            }

            view._owningWindow = window;
            window.PreviewKeyDown += (s, e) =>
            {
                // Only close on Escape when focus isn't in a text box, so
                // clearing the search field with Escape doesn't also close
                // the whole manager.
                if (e.Key == Key.Escape && !(Keyboard.FocusedElement is TextBox))
                {
                    window.Close();
                }
            };

            window.ShowDialog();
        }

        private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UnhookDetailViewModel();

            var row = _viewModel.SelectedGame;
            if (row == null)
            {
                DetailHost.Content = null;
                return;
            }

            _detailViewModel = new CoverManagementViewModel(row.GameId, row.GameName, _repository, _storage, _coverService, _bulkConfigurationService);
            _detailViewModel.PropertyChanged += DetailViewModel_PropertyChanged;

            var detailView = new ManageCoversWindow(
                _detailViewModel, _repository, _steamGridDbProvider, _playniteMetadataProvider, _importService, _localFileCoverAddService, _logger);
            detailView.AttachHost(_dialogs, _owningWindow);

            DetailHost.Content = detailView;
        }

        /// <summary>
        /// Keeps the left-pane row's cover count/status in sync with actions
        /// taken in the embedded detail pane, without rebuilding the whole
        /// list (which would discard the detail pane's transient status
        /// message and force a reselect).
        /// </summary>
        private void DetailViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CoverManagementViewModel.CoverCountText) ||
                e.PropertyName == nameof(CoverManagementViewModel.IsEnabled))
            {
                var gameId = (sender as CoverManagementViewModel)?.GameId;
                if (gameId.HasValue)
                {
                    _viewModel.RefreshGameSummary(gameId.Value);
                }
            }
        }

        private void UnhookDetailViewModel()
        {
            if (_detailViewModel != null)
            {
                _detailViewModel.PropertyChanged -= DetailViewModel_PropertyChanged;
                _detailViewModel = null;
            }
        }

        private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null)
            {
                // XAML pre-selects an item, which fires this handler during
                // InitializeComponent() before the constructor assigns _viewModel.
                return;
            }

            var tag = (FilterCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (Enum.TryParse<ManagedGameFilterMode>(tag, out var mode))
            {
                _viewModel.FilterMode = mode;
            }
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null)
            {
                // Same InitializeComponent()-ordering hazard as FilterCombo_SelectionChanged.
                return;
            }

            var tag = (SortCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (Enum.TryParse<ManagedGameSortMode>(tag, out var mode))
            {
                _viewModel.SortMode = mode;
            }
        }

        private void SourceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null)
            {
                // Same InitializeComponent()-ordering hazard as FilterCombo_SelectionChanged.
                return;
            }

            var tag = (SourceFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            _viewModel.SourceFilter = Enum.TryParse<Domain.CoverSource>(tag, out var source) ? source : (Domain.CoverSource?)null;
        }

        private void EnableButton_Click(object sender, RoutedEventArgs e) => _viewModel.EnableSelected();

        private void DisableButton_Click(object sender, RoutedEventArgs e) => _viewModel.DisableSelected();

        private void ResetOverridesButton_Click(object sender, RoutedEventArgs e) => _viewModel.ResetOverridesForSelected();

        private void SetIntervalButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IntervalHoursText.TryParse(IntervalBox.Text, CultureInfo.CurrentCulture, out var interval, out var error))
            {
                _dialogs.ShowMessage(error);
                return;
            }

            _viewModel.SetIntervalForSelected(interval);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e) => _viewModel.Export();

        private void ImportButton_Click(object sender, RoutedEventArgs e) => _viewModel.Import();

        private void MaintenanceButton_Click(object sender, RoutedEventArgs e)
        {
            var maintenanceViewModel = new MaintenanceViewModel(_maintenanceService, _dialogs, _gameService, _uninstalledGameScanService, _gameUninstallationService);
            new MaintenanceWindow(maintenanceViewModel) { Owner = _owningWindow }.ShowDialog();
            _viewModel.Reload();
        }

        private void ShuffleSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = _viewModel.SelectedGameIds();
            if (ids.Count == 0)
            {
                _viewModel.StatusMessage = "Select at least one game first.";
                return;
            }

            RunBulkShuffle(ids, "Shuffling selected games...");
        }

        private void ShuffleAllInstalledButton_Click(object sender, RoutedEventArgs e)
        {
            RunBulkShuffle(null, "Shuffling installed games...");
        }

        private void RunBulkShuffle(List<Guid> gameIds, string title)
        {
            var progressViewModel = new BulkShuffleProgressViewModel(_bulkShuffleService, gameIds, title);
            new BulkShuffleProgressWindow(progressViewModel) { Owner = _owningWindow }.ShowDialog();
            _viewModel.Reload();
        }
    }
}
