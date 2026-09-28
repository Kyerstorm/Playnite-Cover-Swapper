using System;
using System.Threading.Tasks;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Settings;
using PluginCoverShuffle.Tests.Fakes;
using Xunit;

namespace PluginCoverShuffle.Tests.Settings
{
    public class CoverShufflePluginSettingsViewModelTests
    {
        private static CoverShufflePluginSettingsViewModel Create(
            CoverShuffleSettings settings = null,
            Func<string, Task<SteamGridDbValidationResult>> validate = null,
            FakeCoverShuffleLogger logger = null)
        {
            return new CoverShufflePluginSettingsViewModel(
                settings ?? new CoverShuffleSettings(),
                logger ?? new FakeCoverShuffleLogger(),
                validate ?? (_ => Task.FromResult(new SteamGridDbValidationResult(true, null))));
        }

        [Fact]
        public void IntervalHoursText_StartsFromSavedInterval()
        {
            var viewModel = Create(new CoverShuffleSettings { Interval = TimeSpan.FromHours(6) });

            Assert.Equal("6", viewModel.IntervalHoursText);
            Assert.Null(viewModel.IntervalHoursError);
        }

        [Fact]
        public void IntervalHoursText_Valid_UpdatesInterval()
        {
            var viewModel = Create();

            viewModel.IntervalHoursText = "12";

            Assert.Equal(TimeSpan.FromHours(12), viewModel.Settings.Interval);
            Assert.True(viewModel.VerifySettings(out var errors));
            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("0")]
        [InlineData("-1")]
        public void IntervalHoursText_Invalid_BlocksSavingAndKeepsLastValidInterval(string text)
        {
            // Regression: invalid input used to be silently converted to 24h
            // and saved, despite the tooltip promising it would be rejected.
            var viewModel = Create(new CoverShuffleSettings { Interval = TimeSpan.FromHours(6) });

            viewModel.IntervalHoursText = text;

            Assert.Equal(TimeSpan.FromHours(6), viewModel.Settings.Interval);
            Assert.NotNull(viewModel.IntervalHoursError);
            Assert.False(viewModel.VerifySettings(out var errors));
            Assert.Contains(viewModel.IntervalHoursError, errors);
        }

        [Fact]
        public void IntervalHoursText_FixingAnInvalidValue_ClearsTheError()
        {
            var viewModel = Create();
            viewModel.IntervalHoursText = "abc";

            viewModel.IntervalHoursText = "3";

            Assert.Null(viewModel.IntervalHoursError);
            Assert.True(viewModel.VerifySettings(out _));
        }

        [Fact]
        public async Task ValidateApiKeyAsync_WhileAlreadyValidating_DoesNotStartASecondRequest()
        {
            var calls = 0;
            var pending = new TaskCompletionSource<SteamGridDbValidationResult>();
            var viewModel = Create(
                new CoverShuffleSettings { SteamGridDbApiKey = "key" },
                _ => { calls++; return pending.Task; });

            var first = viewModel.ValidateApiKeyAsync();
            await viewModel.ValidateApiKeyAsync();

            Assert.Equal(1, calls);
            Assert.True(viewModel.IsValidatingApiKey);

            pending.SetResult(new SteamGridDbValidationResult(true, null));
            await first;

            Assert.False(viewModel.IsValidatingApiKey);
            Assert.Equal("API key is valid.", viewModel.ApiKeyValidationStatus);
        }

        [Fact]
        public async Task ValidateApiKeyAsync_UnexpectedException_IsReportedNotThrown()
        {
            var logger = new FakeCoverShuffleLogger();
            var viewModel = Create(
                new CoverShuffleSettings { SteamGridDbApiKey = "secret-key" },
                _ => throw new InvalidOperationException("boom"),
                logger);

            await viewModel.ValidateApiKeyAsync();

            Assert.False(viewModel.IsValidatingApiKey);
            Assert.Contains("Could not check", viewModel.ApiKeyValidationStatus);
            Assert.Single(logger.ErrorMessages);
            Assert.DoesNotContain("secret-key", logger.ErrorMessages[0]);
        }
    }
}
