using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace TradeRouter
{
    /// <summary>
    /// IP allowlist and rate-limiter.
    /// Thread-safe. All checks are O(1) amortized.
    /// </summary>
    public sealed class SecurityManager
    {
        // ── TradingView IPs ──────────────────────────────────────────────────
        private static readonly HashSet<string> TradingViewIPs = new()
        {
            "52.89.214.238",
            "34.212.75.30",
            "54.218.53.128",
            "52.32.178.7"
        };

        public enum IpMode { Any, TradingViewOnly, Custom }

        // ── Settings (set from UI, thread-safe via lock) ─────────────────────
        private readonly object _settingsLock = new();
        private IpMode _ipMode = IpMode.Any;
        private HashSet<string> _customIps = new();
        private string _apiKey = string.Empty;   // empty = disabled
        private int _maxOrdersPerMin = 10;

        // ── Rate limiter: sliding window per source IP ───────────────────────
        // Value: queue of UTC timestamps when orders were accepted
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _rateLimitWindows = new();

        // ── Public Properties ────────────────────────────────────────────────

        public IpMode CurrentIpMode
        {
            get { lock (_settingsLock) { return _ipMode; } }
            set { lock (_settingsLock) { _ipMode = value; } }
        }

        public string ApiKey
        {
            get { lock (_settingsLock) { return _apiKey; } }
            set { lock (_settingsLock) { _apiKey = value ?? string.Empty; } }
        }

        public bool ApiKeyEnabled
        {
            get { lock (_settingsLock) { return !string.IsNullOrEmpty(_apiKey); } }
        }

        public int MaxOrdersPerMin
        {
            get { lock (_settingsLock) { return _maxOrdersPerMin; } }
            set { lock (_settingsLock) { _maxOrdersPerMin = Math.Max(1, Math.Min(60, value)); } }
        }

        /// <summary>
        /// Sets custom IPs from a comma-separated string. Parses and validates each entry.
        /// </summary>
        public void SetCustomIps(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(csv))
            {
                foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string ip = part.Trim();
                    if (IPAddress.TryParse(ip, out _))
                        set.Add(ip);
                }
            }
            lock (_settingsLock) { _customIps = set; }
        }

        public string GetCustomIpsCsv()
        {
            lock (_settingsLock) { return string.Join(", ", _customIps); }
        }

        // ── Validation ────────────────────────────────────────────────────────

        public enum AllowResult { Allowed, BlockedIp, BlockedKey, RateLimited }

        /// <summary>
        /// Checks whether an incoming request should be allowed.
        /// </summary>
        /// <param name="sourceIp">The remote IP address string.</param>
        /// <param name="providedApiKey">The apiKey from the JSON payload (null if not present).</param>
        /// <param name="reason">Human-readable reason if blocked.</param>
        public AllowResult Check(string sourceIp, string? providedApiKey, out string reason)
        {
            // 1. IP check
            if (!IsIpAllowed(sourceIp))
            {
                reason = $"IP {sourceIp} not in allowlist";
                return AllowResult.BlockedIp;
            }

            // 2. API key check
            string key;
            lock (_settingsLock) { key = _apiKey; }
            if (!string.IsNullOrEmpty(key))
            {
                if (string.IsNullOrEmpty(providedApiKey) || providedApiKey != key)
                {
                    reason = $"Invalid or missing API key from {sourceIp}";
                    return AllowResult.BlockedKey;
                }
            }

            // 3. Rate limit check
            int limit;
            lock (_settingsLock) { limit = _maxOrdersPerMin; }
            if (!CheckRateLimit(sourceIp, limit))
            {
                reason = $"Rate limit exceeded from {sourceIp}";
                return AllowResult.RateLimited;
            }

            reason = string.Empty;
            return AllowResult.Allowed;
        }

        private bool IsIpAllowed(string sourceIp)
        {
            IpMode mode;
            HashSet<string> custom;
            lock (_settingsLock)
            {
                mode = _ipMode;
                custom = _customIps;
            }

            return mode switch
            {
                IpMode.Any => true,
                IpMode.TradingViewOnly => TradingViewIPs.Contains(sourceIp),
                IpMode.Custom => custom.Contains(sourceIp),
                _ => false
            };
        }

        private bool CheckRateLimit(string sourceIp, int maxPerMin)
        {
            var window = _rateLimitWindows.GetOrAdd(sourceIp, _ => new Queue<DateTime>());

            lock (window)
            {
                DateTime now = DateTime.UtcNow;
                DateTime cutoff = now.AddMinutes(-1);

                // Evict expired entries
                while (window.Count > 0 && window.Peek() < cutoff)
                    window.Dequeue();

                if (window.Count >= maxPerMin)
                    return false;

                window.Enqueue(now);
                return true;
            }
        }

        /// <summary>
        /// Generates a new UUID v4 API key.
        /// </summary>
        public static string GenerateApiKey() => Guid.NewGuid().ToString("D").ToUpperInvariant();
    }
}
