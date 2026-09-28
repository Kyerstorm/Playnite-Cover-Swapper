using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Infrastructure.Logging;

namespace PluginCoverShuffle.Settings
{
    /// <summary>
    /// Settings view model bridging Playnite's <see cref="ISettings"/> contract
    /// to the plugin's <see cref="CoverShuffleSettings"/> domain model.
    /// UI code binds to this class; it must not touch persistence or file
    /// storage directly.
    /// </summary>
    public class CoverShufflePluginSettingsViewModel : ObservableObject, ISettings
    {
        private readonly Plugin _plugin;
        private readonly ICoverShuffleLogger _logger;
        private readonly Func<string, Task<SteamGridDbValidationResult>> _validateApiKey;
        private readonly Action _onSaved;
        private CoverShuffleSettings _editingClone;
        private CoverShuffleSettings _settings;
        private string _apiKeyValidationStatus;
        private bool _isValidatingApiKey;
        private string _intervalHoursText;
        private string _intervalHoursError;

        public CoverShuffleSettings Settings
        {
            get => _settings;
            set => SetValue(ref _settings, value);
        }

        /// <summary>
        /// The interval text box's raw text. Kept separate from
        /// <see cref="CoverShuffleSettings.Interval"/> so invalid input is
        /// reported (and blocks saving via <see cref="VerifySettings"/>)
        /// instead of being silently replaced with a default.
        /// </summary>
        public string IntervalHoursText
        {
            get => _intervalHoursText;
            set
            {
                SetValue(ref _intervalHoursText, value);
                if (UI.IntervalHoursText.TryParse(value, CultureInfo.CurrentCulture, out var interval, out var error))
                {
                    Settings.Interval = interval;
                }

                IntervalHoursError = error;
            }
        }

        /// <summary>Why <see cref="IntervalHoursText"/> is invalid; null when it is valid.</summary>
        public string IntervalHoursError
        {
            get => _intervalHoursError;
            private set => SetValue(ref _intervalHoursError, value);
        }

        /// <summary>Result text of the last "Validate" click; null until one has run.</summary>
        public string ApiKeyValidationStatus
        {
            get => _apiKeyValidationStatus;
            set => SetValue(ref _apiKeyValidationStatus, value);
        }

        public bool IsValidatingApiKey
        {
            get => _isValidatingApiKey;
            set => SetValue(ref _isValidatingApiKey, value);
        }

        public CoverShufflePluginSettingsViewModel(
            Plugin plugin,
            ICoverShuffleLogger logger,
            Func<string, Task<SteamGridDbValidationResult>> validateApiKey,
            Action onSaved = null)
            : this(plugin.LoadPluginSettings<CoverShuffleSettings>(), logger, validateApiKey)
        {
            _plugin = plugin;
            _onSaved = onSaved;
        }

        /// <summary>Test seam: builds the view model from already-loaded settings, without a Playnite <see cref="Plugin"/>.</summary>
        internal CoverShufflePluginSettingsViewModel(
            CoverShuffleSettings initialSettings,
            ICoverShuffleLogger logger,
            Func<string, Task<SteamGridDbValidationResult>> validateApiKey)
        {
            _logger = logger;
            _validateApiKey = validateApiKey ?? throw new ArgumentNullException(nameof(validateApiKey));
            Settings = initialSettings ?? new CoverShuffleSettings();
            ResetIntervalText();
        }

        /// <summary>Checks the currently entered (not-yet-saved) API key against SteamGridDB.</summary>
        public async Task ValidateApiKeyAsync()
        {
            // The button is also disabled while this runs; this guard covers
            // any click that lands before the binding refreshes.
            if (IsValidatingApiKey)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.SteamGridDbApiKey))
            {
                ApiKeyValidationStatus = "Enter an API key first.";
                return;
            }

            IsValidatingApiKey = true;
            ApiKeyValidationStatus = "Checking...";
            try
            {
                var result = await _validateApiKey(Settings.SteamGridDbApiKey).ConfigureAwait(true);
                ApiKeyValidationStatus = result.IsValid ? "API key is valid." : result.Message;
            }
            catch (Exception ex)
            {
                // Reached from an async void click handler, so anything
                // escaping here would crash Playnite. The client already maps
                // expected network failures; this is the last-resort net.
                // The exception never contains the key, but the key itself is
                // deliberately not logged here either.
                _logger?.Error(ex, "SteamGridDB API key validation failed unexpectedly.");
                ApiKeyValidationStatus = "Could not check the API key. See the Playnite log for details.";
            }
            finally
            {
                IsValidatingApiKey = false;
            }
        }

        public void BeginEdit()
        {
            _editingClone = Serialization.GetClone(Settings);
            ResetIntervalText();
        }

        public void CancelEdit()
        {
            Settings = _editingClone ?? Settings;
            ResetIntervalText();
        }

        private void ResetIntervalText()
        {
            _intervalHoursText = UI.IntervalHoursText.Format(Settings.Interval);
            OnPropertyChanged(nameof(IntervalHoursText));
            IntervalHoursError = null;
        }

        public void EndEdit()
        {
            _plugin.SavePluginSettings(Settings);
            _logger.Info("Cover Shuffle settings saved.");

            // Lets the plugin drop its cached copy so running services pick
            // up the new values on their next read.
            _onSaved?.Invoke();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            // The text box is the source of truth for what the user typed: an
            // unparseable value leaves Settings.Interval at its last valid
            // value, so checking only that would save something the user
            // never saw.
            if (IntervalHoursError != null)
            {
                errors.Add(IntervalHoursError);
            }
            else
            {
                var intervalError = ShuffleIntervalPolicy.Validate(Settings.Interval);
                if (intervalError != null)
                {
                    errors.Add(intervalError);
                }
            }

            return errors.Count == 0;
        }
    }
}
