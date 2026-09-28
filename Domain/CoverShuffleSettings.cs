using System;

namespace PluginCoverShuffle.Domain
{
    /// <summary>
    /// Global default configuration for Cover Shuffle. Per-game configuration
    /// (introduced in a later phase) overrides these defaults on a per-game basis.
    /// </summary>
    public class CoverShuffleSettings
    {
        /// <summary>Whether Cover Shuffle is active by default.</summary>
        public bool Enabled { get; set; }

        /// <summary>Default time between shuffles when <see cref="Mode"/> is <see cref="ShuffleMode.Interval"/>.</summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

        /// <summary>Default shuffle trigger mode.</summary>
        public ShuffleMode Mode { get; set; } = ShuffleMode.Interval;

        /// <summary>
        /// Not read by anything: never repeating the current cover is a fixed
        /// invariant of the shuffle engine, not an option, so the settings UI
        /// no longer shows it. Kept only so existing settings files keep
        /// round-tripping unchanged.
        /// </summary>
        public bool AvoidConsecutiveDuplicates { get; set; } = true;

        /// <summary>Whether a due shuffle should be applied when Playnite starts.</summary>
        public bool ShuffleOnStartup { get; set; } = true;

        /// <summary>Whether a shuffle should occur when a game is launched.</summary>
        public bool ShuffleOnGameLaunch { get; set; }

        /// <summary>How much the plugin should communicate shuffle activity to the user.</summary>
        public NotificationPreference NotificationPreference { get; set; } = NotificationPreference.NotifyOnShuffle;

        /// <summary>
        /// User-supplied SteamGridDB API key. Never hard-coded, never logged,
        /// and never included in exception messages.
        /// </summary>
        public string SteamGridDbApiKey { get; set; }

        /// <summary>
        /// What to do when a game Cover Shuffle hasn't seen before is
        /// installed. Defaults to doing nothing so the plugin never surprises
        /// a user who hasn't visited its settings yet.
        /// </summary>
        public NewGameBehavior NewGameBehavior { get; set; } = NewGameBehavior.DoNothing;
    }
}
