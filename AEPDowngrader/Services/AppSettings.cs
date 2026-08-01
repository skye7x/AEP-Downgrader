using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AEPDowngrader.Services
{
    /// <summary>
    /// Lightweight persistent key/value settings store, functionally equivalent to the
    /// Python app's QSettings("AEPDowngrader", "AEPDowngrader") usage (last_input_directory,
    /// last_output_directory, updates/last_check_ts, updates/skipped_version). Values are
    /// persisted as JSON under %APPDATA%\AEPDowngrader\settings.json.
    /// </summary>
    public class AppSettings
    {
        private readonly string _filePath;
        private Dictionary<string, string> _values = new();

        public AppSettings()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AEPDowngrader");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "settings.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        _values = loaded;
                    }
                }
            }
            catch
            {
                _values = new Dictionary<string, string>();
            }
        }

        private void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Persisting settings is best-effort, same as the original app which
                // never surfaced QSettings write failures to the user.
            }
        }

        public string GetString(string key, string defaultValue = "")
        {
            return _values.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public void SetString(string key, string value)
        {
            _values[key] = value;
            Save();
        }

        public long GetLong(string key, long defaultValue = 0)
        {
            if (_values.TryGetValue(key, out var value) && long.TryParse(value, out long parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        public void SetLong(string key, long value)
        {
            _values[key] = value.ToString();
            Save();
        }

        /// <summary>Get last used directory from settings, mirroring get_last_directory().</summary>
        public string GetLastDirectory(string key = "last_directory")
        {
            string lastDir = GetString(key, "");
            if (!string.IsNullOrEmpty(lastDir) && Directory.Exists(lastDir))
            {
                return lastDir;
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        /// <summary>Save last used directory to settings, mirroring set_last_directory().</summary>
        public void SetLastDirectory(string path, string key = "last_directory")
        {
            if (string.IsNullOrEmpty(path)) return;
            string dirPath = File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path;
            SetString(key, dirPath);
        }
    }
}
