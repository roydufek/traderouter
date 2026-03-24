using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradeRouter.Models;

namespace TradeRouter
{
    /// <summary>
    /// NT8Client v1.0.0 — forwards TradersPost JSON payloads to WebhookOrderStrategy_v1_0_0
    /// running inside NinjaTrader 8 via HTTP POST to a configurable port.
    ///
    /// No ATI/TCP required. Each strategy instance listens on its own port:
    ///   Primary account  → port 7091 (default)
    ///   Second account   → port 7092
    ///   Third account    → port 7093
    ///
    /// The NT8 strategy (WebhookOrderStrategy_v1_0_0) handles all order logic natively.
    /// TradeRouter just securely receives from TradingView and fans out to one or more ports.
    /// </summary>
    public class NT8Client : IDisposable
    {
        private const string NT8_HOST = "127.0.0.1";
        private const int CONNECT_TIMEOUT_MS = 3000;
        private const int SEND_TIMEOUT_MS    = 5000;

        private HttpClient _http;
        private int _port;
        private bool _disposed = false;
        private readonly object _lock = new();

        // ── State Machine ─────────────────────────────────────────────────────

        public enum ConnectionState
        {
            Disconnected,   // gray  — not yet tested
            Connecting,     // amber — health-check in progress
            Connected,      // green — last health-check succeeded
            Failed          // red   — health-check failed (FailReason set)
        }

        private volatile ConnectionState _state = ConnectionState.Disconnected;
        private string _failReason = string.Empty;

        public ConnectionState State   => _state;
        public bool IsConnected        => _state == ConnectionState.Connected;
        public string FailReason       => _failReason;

        public event EventHandler<ConnectionState>? StateChanged;
        public event EventHandler<string>?          LogMessage;

        // ── Constructor ───────────────────────────────────────────────────────

        public NT8Client()
        {
            _port = 7091;
            _http = BuildHttpClient(_port);
        }

        private static HttpClient BuildHttpClient(int port)
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout          = TimeSpan.FromMilliseconds(CONNECT_TIMEOUT_MS),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            var client = new HttpClient(handler)
            {
                Timeout  = TimeSpan.FromMilliseconds(SEND_TIMEOUT_MS),
                BaseAddress = new Uri($"http://{NT8_HOST}:{port}/")
            };
            return client;
        }

        // ── Port management ───────────────────────────────────────────────────

        /// <summary>
        /// Changes the target port. Rebuilds the HTTP client.
        /// Call whenever the user edits the NT8 Strategy Port field.
        /// </summary>
        public void SetPort(int port)
        {
            lock (_lock)
            {
                if (port == _port) return;
                _port = port;
                _http.Dispose();
                _http = BuildHttpClient(port);
                _state = ConnectionState.Disconnected;
                _failReason = string.Empty;
                OnStateChanged(ConnectionState.Disconnected);
                Log($"Port changed to {port}.");
            }
        }

        public int Port => _port;

        // ── Health check (replaces "connect and get accounts") ────────────────

        /// <summary>
        /// Sends a GET / to the NT8 strategy port to verify it's listening.
        /// WebhookOrderStrategy_v1_0_0 responds 405 to GET (it only accepts POST) — that's a valid
        /// "listener is up" response. Any connection/timeout = strategy not running.
        /// </summary>
        public async Task<List<string>> ConnectAndGetAccountsAsync(CancellationToken cancellationToken = default)
        {
            SetState(ConnectionState.Connecting, "");
            Log($"Health-checking NT8 strategy on port {_port}...");

            try
            {
                // GET / — expect either 200 or 405 (method not allowed); both mean "port is open"
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(CONNECT_TIMEOUT_MS);

                var response = await _http.GetAsync("/", cts.Token);

                // Any HTTP response = strategy is listening
                Log($"✓ NT8 strategy responded on port {_port} (HTTP {(int)response.StatusCode}). Ready.");
                SetState(ConnectionState.Connected, "");

                // Return a fake account list indicating the strategy is active.
                // The actual account is configured in the NT8 strategy itself — TradeRouter
                // doesn't need to know the account name anymore.
                return new List<string> { $"NT8Strategy:{_port}" };
            }
            catch (TaskCanceledException)
            {
                string reason = $"NT8 strategy on port {_port} did not respond within {CONNECT_TIMEOUT_MS}ms. Is WebhookOrderStrategy_v1_0_0 loaded in NT8?";
                Log($"✗ {reason}");
                SetState(ConnectionState.Failed, reason);
                throw new TimeoutException(reason);
            }
            catch (HttpRequestException ex)
            {
                string reason = $"Could not reach NT8 strategy on port {_port}: {ex.Message}. Is WebhookOrderStrategy_v1_0_0 loaded in NT8?";
                Log($"✗ {reason}");
                SetState(ConnectionState.Failed, reason);
                throw new InvalidOperationException(reason, ex);
            }
            catch (Exception ex)
            {
                string reason = $"Health check failed: {ex.Message}";
                Log($"✗ {reason}");
                SetState(ConnectionState.Failed, reason);
                throw;
            }
        }

        // ── Order forwarding ──────────────────────────────────────────────────

        /// <summary>
        /// Forwards the full TradersPost JSON payload to the NT8 strategy HTTP listener.
        /// The strategy handles order logic, position tracking, and risk caps internally.
        /// </summary>
        public async Task<string> SendOrderAsync(WebhookPayload payload, string? apiKey = null, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to NT8 strategy. Health-check first.");

            string json = payload.ToForwardJson();
            Log($"→ Forwarding to NT8:{_port}: {json}");

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add API key header if configured in the NT8 strategy
            if (!string.IsNullOrWhiteSpace(apiKey))
                content.Headers.Add("X-Api-Key", apiKey);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(SEND_TIMEOUT_MS);

                var response = await _http.PostAsync("/", content, cts.Token);
                string body  = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                    Log($"← NT8:{_port} OK ({(int)response.StatusCode}): {body}");
                else
                    Log($"← NT8:{_port} {(int)response.StatusCode}: {body}");

                return body;
            }
            catch (TaskCanceledException)
            {
                string msg = $"NT8 strategy on port {_port} timed out during order send.";
                Log($"✗ {msg}");
                // Mark unhealthy — next send attempt will require reconnect
                SetState(ConnectionState.Failed, msg);
                throw new TimeoutException(msg);
            }
            catch (HttpRequestException ex)
            {
                string msg = $"Order send failed: {ex.Message}";
                Log($"✗ {msg}");
                SetState(ConnectionState.Failed, msg);
                throw new InvalidOperationException(msg, ex);
            }
        }

        // ── Fan-out to multiple ports ─────────────────────────────────────────

        /// <summary>
        /// Forwards payload to multiple NT8 strategy ports simultaneously (trade copier).
        /// Fires-and-forgets to additional ports; failures are logged but don't block primary.
        /// Returns result from the primary (first) port.
        /// </summary>
        public async Task<string> SendOrderToPortsAsync(
            WebhookPayload payload,
            IEnumerable<int> ports,
            string? apiKey = null,
            CancellationToken cancellationToken = default)
        {
            string json = payload.ToForwardJson();
            Log($"→ Fan-out to {string.Join(",", ports)}: {json}");

            string primaryResult = "";
            bool first = true;

            foreach (int port in ports)
            {
                if (first)
                {
                    // Primary port — await and return its result
                    try
                    {
                        using var client = BuildHttpClient(port);
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        if (!string.IsNullOrWhiteSpace(apiKey)) content.Headers.Add("X-Api-Key", apiKey);
                        var resp = await client.PostAsync("/", content, cancellationToken);
                        primaryResult = await resp.Content.ReadAsStringAsync(cancellationToken);
                        Log($"← NT8:{port} {(int)resp.StatusCode}: {primaryResult}");
                    }
                    catch (Exception ex) { Log($"✗ NT8:{port} {ex.Message}"); }
                    first = false;
                }
                else
                {
                    // Secondary ports — fire and forget
                    int p = port;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var client = BuildHttpClient(p);
                            using var content = new StringContent(json, Encoding.UTF8, "application/json");
                            if (!string.IsNullOrWhiteSpace(apiKey)) content.Headers.Add("X-Api-Key", apiKey);
                            var resp = await client.PostAsync("/", content, CancellationToken.None);
                            string body2 = await resp.Content.ReadAsStringAsync();
                            Log($"← NT8:{p} {(int)resp.StatusCode}: {body2}");
                        }
                        catch (Exception ex) { Log($"✗ NT8:{p} {ex.Message}"); }
                    });
                }
            }

            return primaryResult;
        }

        // ── Position query (legacy shim — strategy manages position internally) ──

        /// <summary>
        /// Position is now tracked by WebhookOrderStrategy_v1_0_0 internally.
        /// This method exists for UI compatibility — returns Unknown since TradeRouter
        /// no longer needs to track position state.
        /// Use the NT8 strategy's built-in P&L logging and position management instead.
        /// </summary>
        public Task<PositionQueryResult> QueryPositionAsync(string account, string instrument, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PositionQueryResult
            {
                Direction = "Unknown — managed by NT8 strategy",
                Quantity  = 0
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetState(ConnectionState state, string reason)
        {
            _state      = state;
            _failReason = reason;
            OnStateChanged(state);
        }

        public void Disconnect()
        {
            if (_state != ConnectionState.Disconnected)
            {
                SetState(ConnectionState.Disconnected, "");
                Log("Disconnected.");
            }
        }

        private void OnStateChanged(ConnectionState state) => StateChanged?.Invoke(this, state);
        private void Log(string message)                   => LogMessage?.Invoke(this, message);

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _http?.Dispose();
            }
        }
    }

    /// <summary>Result from position query (legacy shim for UI compatibility).</summary>
    public class PositionQueryResult
    {
        public string Direction { get; set; } = "Flat";
        public int    Quantity  { get; set; } = 0;
    }
}
