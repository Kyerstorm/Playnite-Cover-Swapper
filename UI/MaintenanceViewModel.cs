using System;
// Removing this using reintroduces a local Roslyn/MSBuild resolution
// failure on this file (CS0246 on ObservableObject) that appears tied to
// the exact set of using directives present, reproducible even with a bare
// csc.exe invocation outside of MSBuild - keep it even though nothing here
// otherwise needs System.Collections.Generic directly.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using Playnite.SDK;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Services;

namespace PluginCoverShuffle.UI
{
    /// <summary>
    /// Backs the Maintenance window: a health summary plus four selectable
    /// issue/cache lists, three built from <see cref="MaintenanceService.Scan"/>
    /// and the fourth (uninstalled games) from <see cref="UninstalledGameScanService.Scan"/>.
    /// Every destructive action operates only on the items the user has
    /// checked and requires an explicit confirmed click - nothing is ever
    /// deleted automatically. Contains no WPF dependency beyond
    /// <see cref="ObservableObject"/> and <see cref="IDialogsFactory"/>
    /// (used only for the confirmation prompt), so it is testable without a window.
    /// </summary>
    public class MaintenanceViewModel : ObservableObject
    {
        private readonly MaintenanceService _service;
        private readonly IDialogsFactory _dialogs;
        private readonly IPlayniteGameService _gameService;
        private readonly UninstalledGameScanService _uninstalledGameScanService;
        private readonly GameUninstallationService _gameUninstallationService;
        private MaintenanceReport _report;

        public ObservableCollection<MaintenanceIssueItem> MissingCoverItems { get; } = new ObservableCollection<MaintenanceIssueItem>();

        public ObservableCollection<MaintenanceIssueItem> OrphanedFileItems { get; } = new ObservableCollection<MaintenanceIssueItem>();

        public ObservableCollection<MaintenanceIssueItem> CacheFileItems { get; } = new ObservableCollection<MaintenanceIssueItem>();

        public ObservableCollection<MaintenanceIssueItem> UninstalledGameItems { get; } = new ObservableCollection<MaintenanceIssueItem>();

        public int ManagedGamesCount => _report?.ManagedGamesCount ?? 0;

        public int TotalCoversCount => _report?.TotalCoversCount ?? 0;

        public int ValidCoversCount => _report?.ValidCoversCount ?? 0;

        public int MissingFilesCount => MissingCoverItems.Count;

        public int OrphanedFilesCount => OrphanedFileItems.Count;

        public int CacheFilesCount => CacheFileItems.Count;

        public int UninstalledGamesCount => UninstalledGameItems.Count;

        public string CoverStorageSizeText => FormatBytes(_report?.CoverStorageSizeBytes ?? 0);

        public string CacheStorageSizeText => FormatBytes(_report?.CacheStorageSizeBytes ?? 0);

        public bool HasIssues => (_report != null && _report.HasIssues) || UninstalledGameItems.Count > 0;

        public bool IsHealthy => !HasIssues;

        public int IssueCount => (_report?.IssueCount ?? 0) + UninstalledGameItems.Count;

        public bool IsEmpty => (_report == null || _report.IsEmpty) && UninstalledGameItems.Count == 0;

        public int SelectedMissingCount => MissingCoverItems.Count(i => i.IsSelected);

        public int SelectedOrphanedCount => OrphanedFileItems.Count(i => i.IsSelected);

        public int SelectedCacheCount => CacheFileItems.Count(i => i.IsSelected);

        public int SelectedUninstalledGamesCount => UninstalledGameItems.Count(i => i.IsSelected);

        private string _statusMessage;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetValue(ref _statusMessage, value);
        }

        public MaintenanceViewModel(
            MaintenanceService service,
            IDialogsFactory dialogs,
            IPlayniteGameService gameService,
            UninstalledGameScanService uninstalledGameScanService,
            GameUninstallationService gameUninstallationService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _gameService = gameService;
            _uninstalledGameScanService = uninstalledGameScanService ?? throw new ArgumentNullException(nameof(uninstalledGameScanService));
            _gameUninstallationService = gameUninstallationService ?? throw new ArgumentNullException(nameof(gameUninstallationService));
            Scan();
        }

        public void Scan()
        {
            UnhookItems(MissingCoverItems);
            UnhookItems(OrphanedFileItems);
            UnhookItems(CacheFileItems);
            UnhookItems(UninstalledGameItems);

            _report = _service.Scan();

            MissingCoverItems.Clear();
            foreach (var cover in _report.InvalidCoverRecords)
            {
                AddItem(MissingCoverItems, MaintenanceIssueItem.ForMissingCover(cover, ResolveGameName(cover.GameId)));
            }

            OrphanedFileItems.Clear();
            foreach (var filePath in _report.OrphanedCoverFiles)
            {
                AddItem(OrphanedFileItems, MaintenanceIssueItem.ForOrphanedFile(filePath, ResolveGameName(TryExtractGameId(filePath))));
            }

            CacheFileItems.Clear();
            foreach (var filePath in _report.CacheFiles)
            {
                AddItem(CacheFileItems, MaintenanceIssueItem.ForCacheFile(filePath));
            }

            UninstalledGameItems.Clear();
            foreach (var candidate in _uninstalledGameScanService.Scan())
            {
                AddItem(UninstalledGameItems, MaintenanceIssueItem.ForUninstalledGame(candidate.GameId, candidate.GameName, candidate.CoverCount));
            }

            StatusMessage = null;
            RaiseAllPropertiesChanged();
        }

        public void SelectAllMissing(bool selected) => SetAllSelected(MissingCoverItems, selected);

        public void SelectAllOrphaned(bool selected) => SetAllSelected(OrphanedFileItems, selected);

        public void SelectAllCache(bool selected) => SetAllSelected(CacheFileItems, selected);

        public void SelectAllUninstalledGames(bool selected) => SetAllSelected(UninstalledGameItems, selected);

        public void RemoveSelectedMissingRecords()
        {
            var selected = MissingCoverItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            if (!Confirm($"Remove {selected.Count} cover record(s) whose file is missing? The (already-missing) files are unaffected."))
            {
                return;
            }

            _service.RemoveInvalidCoverRecords(selected.Select(i => i.Cover));
            StatusMessage = $"Removed {selected.Count} cover record(s).";
            Scan();
        }

        public void DeleteSelectedOrphanedFiles()
        {
            var selected = OrphanedFileItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            if (!Confirm($"Permanently delete {selected.Count} orphaned cover file(s) from disk? This cannot be undone."))
            {
                return;
            }

            _service.DeleteOrphanedCoverFiles(selected.Select(i => i.FilePath));
            StatusMessage = $"Deleted {selected.Count} orphaned file(s).";
            Scan();
        }

        public void DeleteSelectedCacheFiles()
        {
            var selected = CacheFileItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            if (!Confirm($"Delete {selected.Count} cached file(s)? They will be re-downloaded automatically if needed again."))
            {
                return;
            }

            _service.DeleteCacheFiles(selected.Select(i => i.FilePath));
            StatusMessage = $"Deleted {selected.Count} cache file(s).";
            Scan();
        }

        public void RestoreAndRemoveSelectedUninstalledGames()
        {
            var selected = UninstalledGameItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            if (!Confirm($"Restore the original Playnite cover and remove Cover Shuffle's data for {selected.Count} game(s)? This cannot be undone."))
            {
                return;
            }

            var succeeded = 0;
            var failed = 0;
            foreach (var item in selected)
            {
                var result = _gameUninstallationService.HandleGameUninstalled(item.GameId.Value, item.GameName);
                if (result.Success)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                }
            }

            StatusMessage = failed == 0
                ? $"Cleaned up {succeeded} game(s)."
                : $"Cleaned up {succeeded} game(s); {failed} failed (see log for details).";
            Scan();
        }

        private void SetAllSelected(ObservableCollection<MaintenanceIssueItem> items, bool selected)
        {
            foreach (var item in items)
            {
                item.IsSelected = selected;
            }
        }

        private void AddItem(ObservableCollection<MaintenanceIssueItem> collection, MaintenanceIssueItem item)
        {
            item.PropertyChanged += Item_PropertyChanged;
            collection.Add(item);
        }

        private void UnhookItems(ObservableCollection<MaintenanceIssueItem> collection)
        {
            foreach (var item in collection)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MaintenanceIssueItem.IsSelected))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedMissingCount));
            OnPropertyChanged(nameof(SelectedOrphanedCount));
            OnPropertyChanged(nameof(SelectedCacheCount));
            OnPropertyChanged(nameof(SelectedUninstalledGamesCount));
        }

        private string ResolveGameName(Guid? gameId)
        {
            if (gameId == null)
            {
                return "(unknown game)";
            }

            return _gameService?.GetGameName(gameId.Value) ?? "(game not found in Playnite)";
        }

        /// <summary>
        /// An orphaned file has no cover record to read a game ID from, but
        /// its own path is stable: <see cref="Infrastructure.Storage.CoverStorageLayout.GetGameCoversDirectory"/>
        /// always names the containing folder after the game ID (see
        /// <see cref="Infrastructure.Storage.CoverStorage.SaveCoverFile"/>).
        /// </summary>
        private static Guid? TryExtractGameId(string absoluteFilePath)
        {
            var directoryName = Path.GetFileName(Path.GetDirectoryName(absoluteFilePath));
            return Guid.TryParseExact(directoryName, "N", out var gameId) ? gameId : (Guid?)null;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:0} {units[unitIndex]}"
                : $"{size:0.#} {units[unitIndex]}";
        }

        private void RaiseAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(ManagedGamesCount));
            OnPropertyChanged(nameof(TotalCoversCount));
            OnPropertyChanged(nameof(ValidCoversCount));
            OnPropertyChanged(nameof(MissingFilesCount));
            OnPropertyChanged(nameof(OrphanedFilesCount));
            OnPropertyChanged(nameof(CacheFilesCount));
            OnPropertyChanged(nameof(UninstalledGamesCount));
            OnPropertyChanged(nameof(CoverStorageSizeText));
            OnPropertyChanged(nameof(CacheStorageSizeText));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(IsHealthy));
            OnPropertyChanged(nameof(IssueCount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(SelectedMissingCount));
            OnPropertyChanged(nameof(SelectedOrphanedCount));
            OnPropertyChanged(nameof(SelectedCacheCount));
            OnPropertyChanged(nameof(SelectedUninstalledGamesCount));
        }

        private bool Confirm(string message)
        {
            return _dialogs.ShowMessage(message, "Cover Shuffle", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }
    }
}
