using System;
using System.Windows;

namespace PluginCoverShuffle.UI
{
    public partial class MaintenanceWindow : Window
    {
        private readonly MaintenanceViewModel _viewModel;

        public MaintenanceWindow(MaintenanceViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
        }

        private void SelectAllMissingButton_Click(object sender, RoutedEventArgs e) => _viewModel.SelectAllMissing(true);

        private void RemoveMissingButton_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedMissingRecords();

        private void SelectAllOrphanedButton_Click(object sender, RoutedEventArgs e) => _viewModel.SelectAllOrphaned(true);

        private void DeleteOrphanedButton_Click(object sender, RoutedEventArgs e) => _viewModel.DeleteSelectedOrphanedFiles();

        private void SelectAllCacheButton_Click(object sender, RoutedEventArgs e) => _viewModel.SelectAllCache(true);

        private void DeleteCacheButton_Click(object sender, RoutedEventArgs e) => _viewModel.DeleteSelectedCacheFiles();

        private void SelectAllUninstalledButton_Click(object sender, RoutedEventArgs e) => _viewModel.SelectAllUninstalledGames(true);

        private void RestoreAndRemoveUninstalledButton_Click(object sender, RoutedEventArgs e) => _viewModel.RestoreAndRemoveSelectedUninstalledGames();

        private void RescanButton_Click(object sender, RoutedEventArgs e) => _viewModel.Scan();
    }
}
