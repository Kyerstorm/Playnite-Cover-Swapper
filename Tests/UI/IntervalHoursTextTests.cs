using System;
using System.Globalization;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.UI;
using Xunit;

namespace PluginCoverShuffle.Tests.UI
{
    public class IntervalHoursTextTests
    {
        private static readonly CultureInfo DotDecimal = new CultureInfo("en-GB");
        private static readonly CultureInfo CommaDecimal = new CultureInfo("de-DE");

        [Theory]
        [InlineData("24", 24)]
        [InlineData("1.5", 1.5)]
        [InlineData("  6  ", 6)]
        public void TryParse_ValidHours_ProducesInterval(string text, double expectedHours)
        {
            Assert.True(IntervalHoursText.TryParse(text, DotDecimal, out var interval, out var error));
            Assert.Equal(TimeSpan.FromHours(expectedHours), interval);
            Assert.Null(error);
        }

        [Fact]
        public void TryParse_InvariantDecimalInCommaLocale_IsNotMisreadAsThousands()
        {
            // Regression: Manage Covers displayed "1.5" but parsed with the
            // user's culture, so in de-DE clicking Apply saved 15 hours.
            Assert.True(IntervalHoursText.TryParse("1.5", CommaDecimal, out var interval, out _));
            Assert.Equal(TimeSpan.FromHours(1.5), interval);
        }

        [Fact]
        public void TryParse_CommaDecimalInCommaLocale_IsAccepted()
        {
            Assert.True(IntervalHoursText.TryParse("1,5", CommaDecimal, out var interval, out _));
            Assert.Equal(TimeSpan.FromHours(1.5), interval);
        }

        [Fact]
        public void TryParse_CommaInDotDecimalLocale_IsRejectedNotReadAs15()
        {
            Assert.False(IntervalHoursText.TryParse("1,5", DotDecimal, out _, out var error));
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void TryParse_NotANumber_FailsWithReasonInsteadOfDefaulting(string text)
        {
            Assert.False(IntervalHoursText.TryParse(text, DotDecimal, out var interval, out var error));
            Assert.Equal(TimeSpan.Zero, interval);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("0.01")] // 36 seconds: under the 1-minute minimum.
        public void TryParse_BelowMinimum_Fails(string text)
        {
            Assert.False(IntervalHoursText.TryParse(text, DotDecimal, out _, out var error));
            Assert.Contains("at least 1 minute", error);
        }

        [Theory]
        [InlineData("8761")]
        [InlineData("1e300")] // Would throw OverflowException in TimeSpan.FromHours without the pre-check.
        public void TryParse_AboveMaximum_FailsWithoutThrowing(string text)
        {
            Assert.False(IntervalHoursText.TryParse(text, DotDecimal, out _, out var error));
            Assert.Contains("at most", error);
        }

        [Fact]
        public void TryParse_ExactBounds_AreAccepted()
        {
            Assert.True(IntervalHoursText.TryParse(ShuffleIntervalPolicy.Maximum.TotalHours.ToString(CultureInfo.InvariantCulture), DotDecimal, out var max, out _));
            Assert.Equal(ShuffleIntervalPolicy.Maximum, max);
        }

        [Theory]
        [InlineData(24, "24")]
        [InlineData(1.5, "1.5")]
        public void Format_UsesInvariantDecimal(double hours, string expected)
        {
            Assert.Equal(expected, IntervalHoursText.Format(TimeSpan.FromHours(hours)));
        }

        [Fact]
        public void Format_ThenTryParse_RoundTripsInEveryCulture()
        {
            var text = IntervalHoursText.Format(TimeSpan.FromHours(2.25));

            Assert.True(IntervalHoursText.TryParse(text, CommaDecimal, out var interval, out _));
            Assert.Equal(TimeSpan.FromHours(2.25), interval);
        }
    }
}
