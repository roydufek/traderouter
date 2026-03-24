using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TradeRouter
{
    /// <summary>
    /// Simple INI file reader/writer with section support.
    /// Thread-safe: all public methods lock internally.
    /// </summary>
    public sealed class IniFile
    {
        private readonly string _path;
        private readonly object _lock = new();
        // Dictionary<section, Dictionary<key, value>>
        private Dictionary<string, Dictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);

        public IniFile(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Loads the INI file from disk. Creates with defaults if missing.
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                if (!File.Exists(_path))
                    return;

                string currentSection = string.Empty;
                foreach (string rawLine in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
                        continue;

                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        currentSection = line[1..^1].Trim();
                        if (!_data.ContainsKey(currentSection))
                            _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            string key = line[..eq].Trim();
                            string value = line[(eq + 1)..].Trim();
                            if (!_data.ContainsKey(currentSection))
                                _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            _data[currentSection][key] = value;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Saves all sections/keys to disk.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var sb = new StringBuilder();
                    foreach (var section in _data)
                    {
                        sb.AppendLine($"[{section.Key}]");
                        foreach (var kv in section.Value)
                            sb.AppendLine($"{kv.Key}={kv.Value}");
                        sb.AppendLine();
                    }
                    File.WriteAllText(_path, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Don't crash on save failure
                }
            }
        }

        public string Get(string section, string key, string defaultValue = "")
        {
            lock (_lock)
            {
                if (_data.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var val))
                    return val;
                return defaultValue;
            }
        }

        public void Set(string section, string key, string value)
        {
            lock (_lock)
            {
                if (!_data.ContainsKey(section))
                    _data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _data[section][key] = value;
            }
        }

        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            string val = Get(section, key, defaultValue.ToString());
            return val.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public int GetInt(string section, string key, int defaultValue = 0)
        {
            string val = Get(section, key, defaultValue.ToString());
            return int.TryParse(val, out int result) ? result : defaultValue;
        }

        public void SetBool(string section, string key, bool value) => Set(section, key, value.ToString().ToLower());
        public void SetInt(string section, string key, int value) => Set(section, key, value.ToString());
    }
}
