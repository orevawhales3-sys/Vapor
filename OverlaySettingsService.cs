using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DynamicOverlay.Poc
{
    /// <summary>
    /// Persist and restore the selected OverlayMode in %LOCALAPPDATA%\DynamicOverlayPoc\settings.json.
    /// Decoupled from the UI so Phase 4 can bind its polished mode selector directly to this service.
    /// </summary>
    public sealed class OverlaySettingsService
    {
        private readonly string _dataDirectory;
        private readonly string _settingsFilePath;

        public OverlaySettingsService(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentNullException("dataDirectory");
            }

            _dataDirectory = dataDirectory;
            Directory.CreateDirectory(_dataDirectory);
            _settingsFilePath = Path.Combine(_dataDirectory, "settings.json");
        }

        public string SettingsFilePath
        {
            get { return _settingsFilePath; }
        }

        private bool _cachedCaptureProtectionEnabled = true;
        private bool _hasLoadedCaptureProtection;

        /// <summary>
        /// Loads the persisted mode from settings.json, or returns OverlayMode.Lite by default
        /// for a new installation or if the settings file is absent/invalid.
        /// </summary>
        public OverlayMode LoadMode()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return OverlayMode.Lite;
                }

                string rawJson = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    return OverlayMode.Lite;
                }

                string extracted = ExtractJsonStringField(rawJson, "selectedMode");
                OverlayMode parsed;
                if (OverlayModeExtensions.TryParse(extracted, out parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // Fall back safely to default Lite Mode if settings file cannot be read
            }

            return OverlayMode.Lite;
        }

        /// <summary>
        /// Loads the persisted Capture Protection setting from settings.json.
        /// Defaults to true when the file or field is absent (backward-compatible with Phase 2/3 settings files).
        /// </summary>
        public bool LoadCaptureProtectionEnabled()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    _cachedCaptureProtectionEnabled = true;
                    _hasLoadedCaptureProtection = true;
                    return true;
                }

                string rawJson = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    _cachedCaptureProtectionEnabled = true;
                    _hasLoadedCaptureProtection = true;
                    return true;
                }

                bool parsedBool;
                if (TryExtractJsonBoolField(rawJson, "captureProtectionEnabled", out parsedBool))
                {
                    _cachedCaptureProtectionEnabled = parsedBool;
                    _hasLoadedCaptureProtection = true;
                    return parsedBool;
                }
            }
            catch
            {
                // Fall back safely to enabled if settings file cannot be read
            }

            _cachedCaptureProtectionEnabled = true;
            _hasLoadedCaptureProtection = true;
            return true;
        }

        private string _cachedSelectedTheme = "Dark";
        private bool _hasLoadedSelectedTheme;

        /// <summary>
        /// Loads the persisted theme name from settings.json ("Dark" or "Neon"), defaulting to "Dark".
        /// </summary>
        public string LoadSelectedTheme()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    _cachedSelectedTheme = "Dark";
                    _hasLoadedSelectedTheme = true;
                    return "Dark";
                }

                string rawJson = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    _cachedSelectedTheme = "Dark";
                    _hasLoadedSelectedTheme = true;
                    return "Dark";
                }

                string extracted = ExtractJsonStringField(rawJson, "selectedTheme");
                if (string.Equals(extracted, "Neon", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedSelectedTheme = "Neon";
                    _hasLoadedSelectedTheme = true;
                    return "Neon";
                }
                if (string.Equals(extracted, "Dark", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedSelectedTheme = "Dark";
                    _hasLoadedSelectedTheme = true;
                    return "Dark";
                }
            }
            catch
            {
            }

            _cachedSelectedTheme = "Dark";
            _hasLoadedSelectedTheme = true;
            return "Dark";
        }

        /// <summary>
        /// Atomically persists the selected mode to settings.json while preserving captureProtectionEnabled.
        /// </summary>
        public bool SaveMode(OverlayMode mode)
        {
            bool captureEnabled = _hasLoadedCaptureProtection
                ? _cachedCaptureProtectionEnabled
                : LoadCaptureProtectionEnabled();
            return SaveSettings(mode, captureEnabled);
        }

        /// <summary>
        /// Atomically persists both selectedMode and captureProtectionEnabled to settings.json.
        /// </summary>
        public bool SaveSettings(OverlayMode mode, bool captureProtectionEnabled)
        {
            string theme = _hasLoadedSelectedTheme ? _cachedSelectedTheme : LoadSelectedTheme();
            return SaveSettings(mode, captureProtectionEnabled, theme);
        }

        /// <summary>
        /// Atomically persists selectedMode, captureProtectionEnabled, and selectedTheme to settings.json.
        /// </summary>
        public bool SaveSettings(OverlayMode mode, bool captureProtectionEnabled, string selectedTheme)
        {
            try
            {
                _cachedCaptureProtectionEnabled = captureProtectionEnabled;
                _hasLoadedCaptureProtection = true;
                _cachedSelectedTheme = string.Equals(selectedTheme, "Neon", StringComparison.OrdinalIgnoreCase) ? "Neon" : "Dark";
                _hasLoadedSelectedTheme = true;

                Directory.CreateDirectory(_dataDirectory);
                string modeName = OverlayModeExtensions.ToIdentifier(mode);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"schemaVersion\": 1,");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"selectedMode\": \"{0}\",\n", modeName);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"captureProtectionEnabled\": {0},\n", captureProtectionEnabled ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"selectedTheme\": \"{0}\",\n", _cachedSelectedTheme);
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"updatedAtUtc\": \"{0:O}\"\n", DateTime.UtcNow);
                sb.AppendLine("}");

                string tmpPath = _settingsFilePath + ".tmp";
                File.WriteAllText(tmpPath, sb.ToString(), Encoding.UTF8);
                File.Copy(tmpPath, _settingsFilePath, true);
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                    // Ignore temp cleanup failure
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            string token = "\"" + fieldName + "\"";
            int keyIndex = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return string.Empty;
            }

            int colonIndex = json.IndexOf(':', keyIndex + token.Length);
            if (colonIndex < 0)
            {
                return string.Empty;
            }

            int firstQuote = json.IndexOf('"', colonIndex + 1);
            if (firstQuote < 0)
            {
                return string.Empty;
            }

            int secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0)
            {
                return string.Empty;
            }

            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }

        private static bool TryExtractJsonBoolField(string json, string fieldName, out bool value)
        {
            value = true;
            string token = "\"" + fieldName + "\"";
            int keyIndex = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return false;
            }

            int colonIndex = json.IndexOf(':', keyIndex + token.Length);
            if (colonIndex < 0)
            {
                return false;
            }

            int idx = colonIndex + 1;
            while (idx < json.Length && char.IsWhiteSpace(json[idx]))
            {
                idx++;
            }

            if (idx + 4 <= json.Length &&
                string.Compare(json, idx, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                value = true;
                return true;
            }

            if (idx + 5 <= json.Length &&
                string.Compare(json, idx, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                value = false;
                return true;
            }

            return false;
        }
    }
}
