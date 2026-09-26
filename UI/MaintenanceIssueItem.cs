using System;
// Unused directly, but removing it reintroduces a local Roslyn/MSBuild
// resolution failure on this file (CS0246 on ObservableObject) that appears
// tied to the exact set of using directives present - see the identical
// note in MaintenanceViewModel.cs.
using System.Collections.Generic;
using Playnite.SDK;

namespace PluginCoverShuffle.UI
{
    /// <summary>
    /// The kind of finding a <see cref="MaintenanceIssueItem"/> represents.
    /// Mirrors the three lists on <see cref="Services.MaintenanceReport"/>.
    /// </summary>
    public enum MaintenanceIssueKind
    {
        MissingCoverFile,
        OrphanedFile,
        CacheFile,
        UninstalledGameWithCovers
    }

    /// <summary>
    /// One selectable row in the Maintenance window's issue/cache cards.
    /// Wraps either a cover record with a missing file, an orphaned file
    /// path, a cache file path, or an uninstalled-game candidate - never
    /// more than one of <see cref="Cover"/>/<see cref="FilePath"/>/
    /// <see cref="GameId"/> is meaningful for a given <see cref="Kind"/>.
    /// Carries only the identifying information needed to act on the
    /// underlying finding later (via <see cref="MaintenanceViewModel"/>);
    /// deletion never happens from this class itself.
    /// </summary>
    public class MaintenanceIssueItem : ObservableObject
    {
        public MaintenanceIssueKind Kind { get; }

        /// <summary>The affected game's display name, or a placeholder when it cannot be resolved.</summary>
        public string GameName { get; }

        /// <summary>Short description of what is wrong / what this is, shown under the game name.</summary>
        public string Subtitle { get; }

        /// <summary>The cover record for <see cref="MaintenanceIssueKind.MissingCoverFile"/>; null otherwise.</summary>
        public Domain.Cover Cover { get; }

        /// <summary>The absolute file path for <see cref="MaintenanceIssueKind.OrphanedFile"/> and <see cref="MaintenanceIssueKind.CacheFile"/>; null otherwise.</summary>
        public string FilePath { get; }

        /// <summary>The game's Playnite ID for <see cref="MaintenanceIssueKind.UninstalledGameWithCovers"/>; null otherwise. Needed to call <see cref="Playnite.Integration.GameUninstallationService.HandleGameUninstalled"/>.</summary>
        public Guid? GameId { get; }

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetValue(ref _isSelected, value);
        }

        public static MaintenanceIssueItem ForMissingCover(Domain.Cover cover, string gameName)
        {
            var subtitle = $"{cover.Source} cover • ID {cover.CoverId.ToString("N").Substring(0, 8)} • File is missing";
            return new MaintenanceIssueItem(MaintenanceIssueKind.MissingCoverFile, gameName, subtitle, cover, null, null);
        }

        public static MaintenanceIssueItem ForOrphanedFile(string filePath, string gameName)
        {
            return new MaintenanceIssueItem(MaintenanceIssueKind.OrphanedFile, gameName, "Orphaned file • no matching cover record", null, filePath, null);
        }

        public static MaintenanceIssueItem ForCacheFile(string filePath)
        {
            return new MaintenanceIssueItem(MaintenanceIssueKind.CacheFile, System.IO.Path.GetFileName(filePath), "Cached provider image • re-downloaded automatically if needed", null, filePath, null);
        }

        public static MaintenanceIssueItem ForUninstalledGame(Guid gameId, string gameName, int coverCount)
        {
            var subtitle = $"{coverCount} cover{(coverCount == 1 ? "" : "s")} • Playnite reports this game as not installed";
            return new MaintenanceIssueItem(MaintenanceIssueKind.UninstalledGameWithCovers, gameName, subtitle, null, null, gameId);
        }

        private MaintenanceIssueItem(MaintenanceIssueKind kind, string gameName, string subtitle, Domain.Cover cover, string filePath, Guid? gameId)
        {
            Kind = kind;
            GameName = gameName;
            Subtitle = subtitle;
            Cover = cover;
            FilePath = filePath;
            GameId = gameId;
        }
    }
}
