using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradeRouter.Models;

namespace TradeRouter
{
    /// <summary>
    /// HTTP webhook server using HttpListener.
    /// v2.0.0: added IP allowlist, API key auth, rate limiting, silent drops on security violations.
    /// </summary>
    public class WebhookServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private bool _disposed = false;

        /// <summary>Inject SecurityManager to enable auth + IP + rate-limit checks.</summary>
        public SecurityManager? Security { get; set; }

        public bool IsRunning { get; private set; }

        public event EventHandler<WebhookPayload>? WebhookReceived;
        public event EventHandler<string>? LogMessage;
        public event EventHandler<Exception>? ErrorOccurred;

        /// <summary>
        /// Starts the webhook server on the specified port.
        /// </summary>
        public void Start(int port)
        {
            if (IsRunning)
                throw new InvalidOperationException("Server is already running.");

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/webhook/");
            _listener.Prefixes.Add($"http://localhost:{port}/webhook/");

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
            {
                // Fall back to localhost-only if + prefix is denied (no urlacl registered)
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/webhook/");
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/webhook/");
                _listener.Start();
                Log($"⚠ Listening on localhost only — run 'Register Ports' to enable external access.");
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));

            Log($"Webhook server started on port {port}. Listening at /webhook/");
        }

        /// <summary>
        /// Stops the webhook server gracefully.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts?.Cancel();

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { /* best effort */ }

            _listener = null;
            Log("Webhook server stopped.");
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                    // Handle each request on its own task to avoid blocking the loop
                    _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException ex) when (cancellationToken.IsCancellationRequested || ex.ErrorCode == 995)
                {
                    // Listener stopped - normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Log($"Listener error: {ex.Message}");
                        ErrorOccurred?.Invoke(this, ex);
                    }
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            string sourceIp = request.RemoteEndPoint?.Address?.ToString() ?? "unknown";

            try
            {
                // Only accept POST to /webhook or /webhook/
                if (request.HttpMethod != "POST")
                {
                    await SendResponse(response, 405, "Method Not Allowed");
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    await SendResponse(response, 400, "Empty body");
                    return;
                }

                // Parse JSON first so we can extract apiKey before security check
                WebhookPayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<WebhookPayload>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    Log($"JSON parse error from {sourceIp}: {ex.Message}");
                    await SendResponse(response, 400, $"Invalid JSON: {ex.Message}");
                    return;
                }

                if (payload == null || string.IsNullOrWhiteSpace(payload.Action))
                {
                    await SendResponse(response, 400, "Invalid payload: missing action");
                    return;
                }

                // ── Security checks ──────────────────────────────────────────
                if (Security != null)
                {
                    var result = Security.Check(sourceIp, payload.ApiKey, out string reason);
                    if (result != SecurityManager.AllowResult.Allowed)
                    {
                        // Silent drop — close without sending any response body
                        Log($"BLOCKED [{result}] {reason}");
                        try { response.Abort(); } catch { /* best effort */ }
                        return;
                    }
                }

                Log($"Received webhook [{sourceIp}]: {body}");

                // Fire event on a background thread
                await Task.Run(() => WebhookReceived?.Invoke(this, payload));

                await SendResponse(response, 200, "OK");
            }
            catch (Exception ex)
            {
                Log($"Request handler error: {ex.Message}");
                try { await SendResponse(response, 500, "Internal server error"); } catch { /* best effort */ }
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        private static async Task SendResponse(HttpListenerResponse response, int statusCode, string body)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "text/plain; charset=utf-8";
                byte[] buffer = Encoding.UTF8.GetBytes(body);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
                response.OutputStream.Close();
            }
            catch { /* best effort on response write */ }
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
            }
        }
    }
}
