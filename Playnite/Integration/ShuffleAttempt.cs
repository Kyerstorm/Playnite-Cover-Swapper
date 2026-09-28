using PluginCoverShuffle.Domain;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>What happened to one game during a startup shuffle pass.</summary>
    public enum ShuffleAttemptOutcome
    {
        /// <summary>Not attempted: disabled, startup shuffling off for this game, or not installed.</summary>
        Skipped,
        Shuffled,
        Failed
    }

    /// <summary>
    /// The outcome of one game's startup shuffle, carrying what the caller
    /// needs to build a single aggregated notification (the game's name and
    /// its effective notification preference) instead of notifying per game.
    /// </summary>
    public class ShuffleAttempt
    {
        public static readonly ShuffleAttempt Skipped = new ShuffleAttempt(ShuffleAttemptOutcome.Skipped, null, null, NotificationPreference.Silent);

        private ShuffleAttempt(ShuffleAttemptOutcome outcome, string gameName, string message, NotificationPreference preference)
        {
            Outcome = outcome;
            GameName = gameName;
            Message = message;
            Preference = preference;
        }

        public ShuffleAttemptOutcome Outcome { get; }

        public string GameName { get; }

        /// <summary>User-facing failure reason; null unless <see cref="Outcome"/> is <see cref="ShuffleAttemptOutcome.Failed"/>.</summary>
        public string Message { get; }

        public NotificationPreference Preference { get; }

        public static ShuffleAttempt Shuffled(string gameName, NotificationPreference preference) =>
            new ShuffleAttempt(ShuffleAttemptOutcome.Shuffled, gameName, null, preference);

        public static ShuffleAttempt Failed(string gameName, string message, NotificationPreference preference) =>
            new ShuffleAttempt(ShuffleAttemptOutcome.Failed, gameName, message, preference);
    }
}
