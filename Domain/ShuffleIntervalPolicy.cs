using System;

namespace PluginCoverShuffle.Domain
{
    /// <summary>
    /// Central definition of the allowed shuffle-interval range. Every place
    /// that accepts an interval (global settings, a game's override, bulk
    /// "Set Interval") must validate against this policy rather than
    /// applying its own bounds.
    /// </summary>
    public static class ShuffleIntervalPolicy
    {
        /// <summary>Shortest allowed interval; anything smaller would shuffle on practically every trigger.</summary>
        public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Longest allowed interval. Without an upper bound a huge value makes
        /// <c>DateTime.UtcNow.Add(interval)</c> throw when the next shuffle
        /// time is calculated, so it is rejected up front instead.
        /// </summary>
        public static readonly TimeSpan Maximum = TimeSpan.FromDays(365);

        /// <summary>Returns null when <paramref name="interval"/> is allowed, otherwise a concise user-facing reason.</summary>
        public static string Validate(TimeSpan interval)
        {
            if (interval < Minimum)
            {
                return "Shuffle interval must be at least 1 minute.";
            }

            if (interval > Maximum)
            {
                return $"Shuffle interval can be at most {Maximum.TotalHours:0} hours (1 year).";
            }

            return null;
        }
    }
}
