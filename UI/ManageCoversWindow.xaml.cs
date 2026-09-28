using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using PluginCoverShuffle.Domain.Providers;
using PluginCoverShuffle.Infrastructure.Logging;
using PluginCoverShuffle.Infrastructure.Persistence;
using PluginCoverShuffle.Infrastructure.Providers.SteamGridDb;
using PluginCoverShuffle.Services;

namespace PluginCoverShuffle.UI
{
    /// <summary>
    /// The plugin's per-game "Cover Shuffle" control centre: status,
    /// scheduling, the cover pool, and every action available for that one
    /// game. A <see cref="UserControl"/> rather than a <see cref="Window"/>
    /// so it can be hosted inside a window created by
    /// <see cref="IDialogsFactory.CreateWindow"/>, matching Playnite's own
    /// theme instead of the plain WPF default (see <see cref="ShowDialog"/>).
    /// </summary>
    public partial class ManageCoversWindow : UserControl
    {
        private readonly CoverManagementViewModel _viewModel;
        private readonly ICoverShuffleRepository _repository;
        private readonly ICoverProvider _steamGridDbProvider;
        private readonly ICoverProvider _playniteMetadataProvider;
        private readonly CoverImportService _importService;
        private readonly LocalFileCoverAddService _localFileCoverAddService;
        private readonly ICoverShuffleLogger _logger;
        private IDialogsFactory _dialogs;
        private Window _owningWindow;
        private bool _isReplacingCover;

        public ManageCoversWindow(
            CoverManagementViewModel viewModel,
            ICoverShuffleRepository repository,
            ICoverProvider steamGridDbProvider,
            ICoverProvider playniteMetadataProvider,
            CoverImportService importService,
            LocalFileCoverAddService localFileCoverAddService,
            ICoverShuffleLogger logger)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _steamGridDbProvider = steamGridDbProvider;
            _playniteMetadataProvider = playniteMetadataProvider;
            _importService = importService;
            _localFileCoverAddService = localFileCoverAddService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            DataContext = _viewModel;

            AddSteamGridDbButton.Visibility = _steamGridDbProvider != null ? Visibility.Visible : Visibility.Collapsed;
            AddPlayniteMetadataButton.Visibility = _playniteMetadataProvider != null ? Visibility.Visible : Visibility.Collapsed;

            RefreshIntervalBox();
        }

        /// <summary>
        /// Wires up the dialogs factory and owning window when this panel is
        /// hosted directly (e.g. embedded as the Cover Shuffle Manager's
        /// detail pane) rather than shown through the <see cref="ShowDialog"/>
        /// helper below. Without this, "Add Cover" actions that need a
        /// dialogs factory or an owner window silently no-op.
        /// </summary>
        public void AttachHost(IDialogsFactory dialogs, Window owningWindow)
        {
            _dialogs = dialogs;
            _owningWindow = owningWindow;
        }

        /// <summary>Creates and shows this panel in a Playnite-themed modal window.</summary>
        public static void ShowDialog(
            IDialogsFactory dialogs,
            CoverManagementViewModel viewModel,
            ICoverShuffleRepository repository,
            ICoverProvider steamGridDbProvider,
            ICoverProvider playniteMetadataProvider,
            CoverImportService importService,
            LocalFileCoverAddService localFileCoverAddService,
            ICoverShuffleLogger logger)
        {
            if (dialogs == null)
            {
                throw new ArgumentNullException(nameof(dialogs));
            }

            var view = new ManageCoversWindow(viewModel, repository, steamGridDbProvider, playniteMetadataProvider, importService, localFileCoverAddService, logger);
            view._dialogs = dialogs;

            var window = dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = true
            });

            window.Title = $"{viewModel.GameName} - Cover Shuffle";
            window.Content = view;
            window.Height = 680;
            window.Width = 720;
            window.MinHeight = 480;
            window.MinWidth = 560;
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
                if (e.Key == Key.Escape)
                {
                    window.Close();
                }
            };

            window.ShowDialog();
        }

        private void RefreshIntervalBox()
        {
            IntervalBox.Text = IntervalHoursText.Format(TimeSpan.FromHours(_viewModel.CurrentIntervalHours));
        }

        private void ToggleEnabledButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleEnabled();
            RefreshIntervalBox();
        }

        private void ApplyIntervalButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IntervalHoursText.TryParse(IntervalBox.Text, CultureInfo.CurrentCulture, out var interval, out var error))
            {
                _dialogs?.ShowMessage(error);
                return;
            }

            _viewModel.SetInterval(interval);
            RefreshIntervalBox();
        }

        private void ResetToGlobalDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ResetToGlobalDefaults();
            RefreshIntervalBox();
        }

        private void ShuffleNowButton_Click(object sender, RoutedEventArgs e) => _viewModel.ShuffleNow();

        private void RestoreOriginalButton_Click(object sender, RoutedEventArgs e) => _viewModel.RestoreOriginal();

        /// <summary>
        /// Single click selects a card (bound via ListBox.SelectedItem);
        /// double click opens the larger preview (Stage 2), matching the
        /// spec's "single click = select, double click = preview" model.
        /// "Set as Current" (the old double-click behaviour) is still one
        /// click away via the single-selection action bar / context menu.
        /// </summary>
        private void CoversList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ShowPreview(_viewModel.SelectedCover);
        }

        /// <summary>Enter applies the selected cover; Escape is handled by the owning window (see <see cref="ShowDialog"/>).</summary>
        private void CoversList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _viewModel.ChooseSelectedCover();
                e.Handled = true;
            }
        }

        /// <summary>
        /// ListBox.SelectedItems is not a bindable property, so the
        /// multi-selection is pushed into the view model here, mechanically,
        /// with no business logic - see <see cref="CoverManagementViewModel.SetSelection"/>.
        /// </summary>
        private void CoversList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SetSelection(CoversList.SelectedItems.Cast<CoverDisplayItem>());
        }

        private void SelectAllCoversButton_Click(object sender, RoutedEventArgs e) => CoversList.SelectAll();

        private void ClearCoverSelectionButton_Click(object sender, RoutedEventArgs e) => CoversList.UnselectAll();

        private void RemoveSelectedCoverButton_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedCover();

        private void UseSelectedCoverButton_Click(object sender, RoutedEventArgs e) => _viewModel.ChooseSelectedCover();

        private void PreviewSelectedCoverButton_Click(object sender, RoutedEventArgs e) => ShowPreview(_viewModel.SelectedCover);

        private async void ReplaceSelectedCoverButton_Click(object sender, RoutedEventArgs e) => await ReplaceCoverAsync(_viewModel.SelectedCover);

        private void EnableSelectedCoversButton_Click(object sender, RoutedEventArgs e) => _viewModel.EnableSelectedCovers();

        private void DisableSelectedCoversButton_Click(object sender, RoutedEventArgs e) => _viewModel.DisableSelectedCovers();

        private void RemoveSelectedCoversButton_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedCovers();

        /// <summary>
        /// Opens the cover preview positioned on <paramref name="cover"/>,
        /// with Previous/Next navigation across this game's whole pool and a
        /// "Set as Current" action that reuses the exact same
        /// <see cref="CoverManagementViewModel.ChooseCover"/> path as every
        /// other "set as current" entry point - the preview never applies a
        /// cover on its own. Closing the dialog otherwise leaves state
        /// unchanged; refreshes this window once closed in case a cover was
        /// applied from inside the preview.
        /// </summary>
        private void ShowPreview(CoverDisplayItem cover)
        {
            if (cover == null)
            {
                return;
            }

            var covers = _viewModel.Covers;
            var startIndex = Math.Max(0, covers.IndexOf(cover));
            var previewViewModel = new CoverPreviewViewModel(_viewModel.GameName, covers, startIndex, coverId => _viewModel.ChooseCover(coverId));
            new CoverPreviewWindow(previewViewModel) { Owner = _owningWindow }.ShowDialog();
            _viewModel.Reload();
        }

        /// <summary>
        /// Generalized "Replace" for the context menu / action bar: reuses
        /// the exact same safe-replace pipeline as "Locate Replacement"
        /// (<see cref="LocalFileCoverAddService.ReplaceFromFile"/> ->
        /// <see cref="CoverImportService.ReplaceFile"/>), just available for
        /// any cover, not only ones already flagged missing/corrupt.
        /// </summary>
        private async System.Threading.Tasks.Task ReplaceCoverAsync(CoverDisplayItem cover)
        {
            if (_localFileCoverAddService == null || _dialogs == null || cover == null)
            {
                return;
            }

            if (_isReplacingCover)
            {
                return;
            }

            var coverId = cover.CoverId;
            var gameId = _viewModel.GameId;
            var filePath = _dialogs.SelectImagefile();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await RunCoverReplacementAsync("Replacing the cover file", () =>
            {
                var result = _localFileCoverAddService.ReplaceFromFile(gameId, coverId, filePath);
                return result.IsSuccess ? null : result.Message;
            });
        }

        /// <summary>
        /// Shared wrapper for the file-replacing actions, which are started
        /// from async void handlers: an exception escaping one would crash
        /// Playnite, and a second click while the first is still copying
        /// could overwrite the same cover file concurrently.
        /// </summary>
        /// <param name="replace">Runs off the UI thread; returns a user-facing error, or null on success.</param>
        private async System.Threading.Tasks.Task RunCoverReplacementAsync(string operationDescription, Func<string> replace)
        {
            _isReplacingCover = true;
            try
            {
                var errorMessage = await System.Threading.Tasks.Task.Run(replace);
                if (errorMessage != null)
                {
                    _dialogs?.ShowMessage(errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{operationDescription} failed unexpectedly for game '{_viewModel.GameId}'.");
                _dialogs?.ShowMessage("The cover could not be replaced. See the Playnite log for details.");
            }
            finally
            {
                _isReplacingCover = false;
                _viewModel.Reload();
            }
        }

        /// <summary>
        /// Executed handlers for the cover card's context menu / favourite
        /// commands (see CoverCardCommands.cs): the command's
        /// CommandParameter carries the CoverDisplayItem the card/menu item
        /// was invoked for.
        /// </summary>
        private void CoverCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = e.Parameter is CoverDisplayItem;

        private void PreviewCoverCommand_Executed(object sender, ExecutedRoutedEventArgs e) => ShowPreview(e.Parameter as CoverDisplayItem);

        private void SetAsCurrentCoverCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is CoverDisplayItem cover)
            {
                _viewModel.ChooseCover(cover.CoverId);
            }
        }

        private void EnableCoverCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is CoverDisplayItem cover)
            {
                _viewModel.ToggleCoverEnabled(cover.CoverId);
            }
        }

        private void DisableCoverCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is CoverDisplayItem cover)
            {
                _viewModel.ToggleCoverEnabled(cover.CoverId);
            }
        }

        private async void ReplaceCoverCommand_Executed(object sender, ExecutedRoutedEventArgs e) => await ReplaceCoverAsync(e.Parameter as CoverDisplayItem);

        private void RemoveCoverCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is CoverDisplayItem cover)
            {
                _viewModel.Remove(cover.CoverId);
            }
        }

        private void ToggleFavoriteCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is CoverDisplayItem cover)
            {
                _viewModel.ToggleFavorite(cover.CoverId);
            }
        }

        private void AddSteamGridDbButton_Click(object sender, RoutedEventArgs e)
        {
            if (_steamGridDbProvider == null || _importService == null || _dialogs == null)
            {
                return;
            }

            var searchViewModel = new SteamGridDbSearchViewModel(
                _viewModel.GameId, _viewModel.GameName, _steamGridDbProvider, _importService, _repository, _logger);
            SteamGridDbSearchView.ShowDialog(_dialogs, searchViewModel);
            _viewModel.Reload();
        }

        private void AddPlayniteMetadataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playniteMetadataProvider == null || _importService == null)
            {
                return;
            }

            var metadataViewModel = new PlayniteMetadataCoverViewModel(
                _viewModel.GameId, _viewModel.GameName, _playniteMetadataProvider, _importService, _repository, _logger);
            new PlayniteMetadataCoverWindow(metadataViewModel) { Owner = _owningWindow }.ShowDialog();
            _viewModel.Reload();
        }

        private void AddLocalFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_localFileCoverAddService == null || _dialogs == null)
            {
                return;
            }

            var viewModel = new AddLocalCoversViewModel(_viewModel.GameId, _repository, _localFileCoverAddService, _logger);
            AddLocalCoversWindow.ShowDialog(_dialogs, viewModel, _owningWindow);
            _viewModel.Reload();
        }

        private async void RestoreFromSteamGridDbForSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var steamGridDbProvider = _steamGridDbProvider as SteamGridDbCoverProvider;
            if (steamGridDbProvider == null || _importService == null || _dialogs == null || _viewModel.SelectedCover == null || _isReplacingCover)
            {
                return;
            }

            var coverId = _viewModel.SelectedCover.CoverId;
            var gameId = _viewModel.GameId;

            await RunCoverReplacementAsync("Restoring a cover from the SteamGridDB cache", () =>
            {
                var cover = _repository.GetCover(gameId, coverId);
                if (cover == null)
                {
                    return "This cover no longer exists.";
                }

                if (!steamGridDbProvider.TryGetCachedFile(cover.SourceId, out var cachedPath))
                {
                    return "This cover's cached artwork is no longer available locally. Use \"+ SteamGridDB\" to search for replacement artwork.";
                }

                var result = _importService.ReplaceFile(gameId, coverId, cachedPath);
                return result.IsSuccess ? null : result.Message;
            });
        }
    }
}
