using System;
using System.Globalization;
using PluginCoverShuffle.Domain;

namespace PluginCoverShuffle.UI
{
    /// <summary>
    /// Shared parse/format for every "interval (hours)" text box, so they all
    /// accept the same input and never silently substitute a default.
    /// </summary>
    public static class IntervalHoursText
    {
        /// <summary>Formats with an invariant '.' decimal separator, the form <see cref="TryParse"/> always accepts.</summary>
        public static string Format(TimeSpan interval) =>
            Math.Round(interval.TotalHours, 2).ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses hours typed by the user. The invariant form is tried first
        /// because that is what <see cref="Format"/> displays; the user's own
        /// culture is the fallback so e.g. "1,5" still works in a comma-decimal
        /// locale. Neither culture's thousands separator is accepted, so "1,5"
        /// can never be misread as 15 in a '.'-decimal locale.
        /// </summary>
        /// <param name="error">A concise user-facing reason when parsing or validation fails; otherwise null.</param>
        public static bool TryParse(string text, CultureInfo userCulture, out TimeSpan interval, out string error)
        {
            interval = TimeSpan.Zero;
            var trimmed = text?.Trim();

            if (string.IsNullOrEmpty(trimmed)
                || !(double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
                     || double.TryParse(trimmed, NumberStyles.Float, userCulture ?? CultureInfo.InvariantCulture, out hours))
                || double.IsNaN(hours) || double.IsInfinity(hours))
            {
                error = "Enter the shuffle interval as a number of hours, e.g. 24 or 1.5.";
                return false;
            }

            // Range-check in hours before converting: TimeSpan.FromHours throws
            // OverflowException for values far beyond the policy maximum.
            if (hours > ShuffleIntervalPolicy.Maximum.TotalHours || hours < 0)
            {
                error = ShuffleIntervalPolicy.Validate(hours < 0 ? TimeSpan.Zero : TimeSpan.MaxValue);
                return false;
            }

            var candidate = TimeSpan.FromHours(hours);
            error = ShuffleIntervalPolicy.Validate(candidate);
            if (error != null)
            {
                return false;
            }

            interval = candidate;
            return true;
        }
    }
}
