using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using PluginCoverShuffle.Domain;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// Surfaces shuffle activity as Playnite notifications, respecting each
    /// game's effective <see cref="NotificationPreference"/>. The only class
    /// that touches <see cref="INotificationsAPI"/>.
    /// </summary>
    public class CoverShuffleNotificationService
    {
        private readonly INotificationsAPI _notifications;

        public CoverShuffleNotificationService(INotificationsAPI notifications)
        {
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        }

        /// <summary>
        /// A game's own notification id, reused across calls: a later
        /// shuffle for the same game replaces its previous notification
        /// instead of piling up unread ones.
        /// </summary>
        private static string NotificationId(Guid gameId) => "coverShuffle-" + gameId.ToString("N");

        /// <summary>
        /// Stable id for the aggregate "missing covers found" notification:
        /// not tied to any one game, so repeated startup checks replace the
        /// previous notification instead of piling up.
        /// </summary>
        private const string MissingCoversNotificationId = "coverShuffle-maintenance";

        /// <summary>
        /// Stable id for the aggregate "uninstalled games with covers found"
        /// notification: not tied to any one game, so repeated startup
        /// checks replace the previous notification instead of piling up.
        /// </summary>
        private const string UninstalledGamesNotificationId = "coverShuffle-uninstalledGames";

        /// <summary>Stable ids for the aggregated startup-shuffle notifications, so each startup replaces the previous one.</summary>
        private const string StartupShuffledNotificationId = "coverShuffle-startupShuffled";
        private const string StartupFailedNotificationId = "coverShuffle-startupFailed";

        public void NotifyShuffled(Guid gameId, string gameName, NotificationPreference preference)
        {
            if (preference != NotificationPreference.NotifyOnShuffle)
            {
                return;
            }

            _notifications.Add(NotificationId(gameId), $"Cover Shuffle rotated the cover for \"{gameName}\".", NotificationType.Info);
        }

        public void NotifyShuffleFailed(Guid gameId, string gameName, string message, NotificationPreference preference)
        {
            if (preference == NotificationPreference.Silent)
            {
                return;
            }

            _notifications.Add(NotificationId(gameId), $"Cover Shuffle could not shuffle \"{gameName}\": {message}", NotificationType.Error);
        }

        /// <summary>Raised once at startup when maintenance detects one or more covers whose files are missing.</summary>
        public void NotifyMissingCovers(int missingCount, NotificationPreference preference)
        {
            if (preference == NotificationPreference.Silent)
            {
                return;
            }

            _notifications.Add(
                MissingCoversNotificationId,
                $"Cover Shuffle found {missingCount} cover(s) with a missing file. Open the Cover Shuffle Manager's Maintenance tool to review them.",
                NotificationType.Error);
        }

        /// <summary>Raised once at startup when the uninstalled-game scan finds one or more managed games that appear uninstalled.</summary>
        public void NotifyUninstalledGamesWithCovers(int count, NotificationPreference preference)
        {
            if (preference == NotificationPreference.Silent)
            {
                return;
            }

            _notifications.Add(
                UninstalledGamesNotificationId,
                $"Cover Shuffle found {count} managed game(s) that appear uninstalled. Open the Cover Shuffle Manager's Maintenance tool to review them.",
                // Informational, not an error: nothing failed, the user just
                // has plugin data for games that are no longer installed.
                NotificationType.Info);
        }

        /// <summary>
        /// One pair of notifications for a whole startup pass instead of one
        /// per game, so shuffling a large library at startup can't flood the
        /// notification panel. The caller has already filtered each game by
        /// its own preference (only NotifyOnShuffle games in
        /// <paramref name="shuffledGameNames"/>, no Silent games in
        /// <paramref name="failures"/>).
        /// </summary>
        /// <param name="failures">Game name → user-facing failure reason.</param>
        public void NotifyStartupShuffleSummary(IReadOnlyList<string> shuffledGameNames, IReadOnlyList<KeyValuePair<string, string>> failures)
        {
            if (shuffledGameNames != null && shuffledGameNames.Count > 0)
            {
                var text = shuffledGameNames.Count == 1
                    ? $"Cover Shuffle rotated the cover for \"{shuffledGameNames[0]}\"."
                    : $"Cover Shuffle rotated the covers for {shuffledGameNames.Count} games.";
                _notifications.Add(StartupShuffledNotificationId, text, NotificationType.Info);
            }

            if (failures != null && failures.Count > 0)
            {
                var text = failures.Count == 1
                    ? $"Cover Shuffle could not shuffle \"{failures[0].Key}\": {failures[0].Value}"
                    : $"Cover Shuffle could not shuffle {failures.Count} games: {DescribeNames(failures.Select(f => f.Key).ToList())}. Open each game's Manage Covers window to see why.";
                _notifications.Add(StartupFailedNotificationId, text, NotificationType.Error);
            }
        }

        /// <summary>First few names, then "and N more", so the text stays readable for a large library.</summary>
        private static string DescribeNames(IReadOnlyList<string> names)
        {
            const int maxNamed = 3;
            var named = string.Join(", ", names.Take(maxNamed).Select(n => $"\"{n}\""));
            return names.Count > maxNamed ? $"{named} and {names.Count - maxNamed} more" : named;
        }
    }
}
