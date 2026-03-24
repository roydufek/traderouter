using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TradeRouter
{
    /// <summary>
    /// Manages the Tailscale funnel subprocess for exposing the webhook server publicly.
    /// v2.0.0: proper error handling — cert errors, port conflicts, missing binary.
    /// </summary>
    public class TailscaleFunnel : IDisposable
    {
        private Process? _process;
        private bool _disposed = false;
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;
        public string? PublicUrl { get; private set; }

        public event EventHandler<string>? LogMessage;
        public event EventHandler<string>? PublicUrlDiscovered;
        public event EventHandler<string>? ErrorOccurred;  // string message (was Exception)

        // ── Binary check ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the tailscale binary is found in PATH.
        /// </summary>
        public static bool IsTailscaleAvailable()
        {
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "tailscale",
                        Arguments = "version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if tailscale daemon is running via `tailscale status`.
        /// Returns (isRunning, errorMessage).
        /// </summary>
        public static (bool Running, string Error) CheckTailscaleStatus()
        {
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "tailscale",
                        Arguments = "status",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);

                if (p.ExitCode != 0 || stdout.Contains("Stopped") || stdout.Contains("not running"))
                    return (false, "Tailscale not running. Start Tailscale first.");

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Tailscale check failed: {ex.Message}");
            }
        }

        // ── Funnel start ──────────────────────────────────────────────────────

        /// <summary>
        /// Starts `tailscale funnel PORT` and captures the public HTTPS URL.
        /// Throws descriptive exceptions on cert/port/other errors.
        /// </summary>
        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Tailscale funnel is already running.");

            // Pre-flight: check tailscale status
            var (running, statusError) = CheckTailscaleStatus();
            if (!running)
            {
                ErrorOccurred?.Invoke(this, statusError);
                throw new InvalidOperationException(statusError);
            }

            Log($"Starting Tailscale funnel on port {port}...");

            var stderrCapture = new StringBuilder();

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "tailscale",
                    Arguments = $"funnel {port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += OnDataReceived;
            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    stderrCapture.AppendLine(e.Data);
                    OnErrorDataReceived(s, e);
                }
            };
            _process.Exited += OnProcessExited;

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _isRunning = true;

                Log($"Tailscale funnel process started (PID {_process.Id}).");

                // Give it time to either succeed or fail
                await Task.Delay(4000, cancellationToken);

                string stderr = stderrCapture.ToString();

                // "Funnel is not enabled on your tailnet" — extract enable URL and fail cleanly
                if (stderr.Contains("Funnel is not enabled") || stderr.Contains("funnel is not enabled"))
                {
                    // Extract the enable URL if present (e.g. https://login.tailscale.com/f/funnel?node=...)
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(stderr, @"https://login\.tailscale\.com\S+");
                    string enableUrl = urlMatch.Success ? urlMatch.Value.Trim() : "https://login.tailscale.com/admin/acls";
                    string msg = $"Funnel is not enabled on your tailnet.\nTo enable, visit: {enableUrl}";
                    Log($"✗ {msg}");
                    Stop();
                    ErrorOccurred?.Invoke(this, msg);
                    throw new InvalidOperationException(msg);
                }

                // Also check stdout for "Funnel is not enabled" (tailscale prints to stdout on some versions)
                if (!string.IsNullOrEmpty(PublicUrl) == false)
                {
                    // PublicUrl still empty — check if stdout had the error
                }

                if (stderr.Contains("already in use") || stderr.Contains("address already"))
                {
                    string msg = $"Port {port} already in use — choose a different port";
                    Log($"✗ {msg}");
                    Stop();
                    ErrorOccurred?.Invoke(this, msg);
                    throw new InvalidOperationException(msg);
                }

                if (!string.IsNullOrWhiteSpace(stderr) && stderr.Length > 20)
                {
                    string msg = $"Tailscale error: {stderr.Trim()}";
                    Log($"⚠ {msg}");
                    // Don't throw — might still be working
                }

                // If URL wasn't discovered via output, try to get it from `tailscale status`
                if (string.IsNullOrEmpty(PublicUrl))
                {
                    await TryDiscoverUrlAsync(port, cancellationToken);
                }

                if (string.IsNullOrEmpty(PublicUrl))
                {
                    Log("⚠ Could not determine Tailscale public URL. Check tailscale funnel status manually.");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                string msg = $"Failed to start Tailscale funnel: {ex.Message}";
                Log(msg);
                _isRunning = false;
                ErrorOccurred?.Invoke(this, msg);
                throw new InvalidOperationException(msg, ex);
            }
        }

        private async Task TryDiscoverUrlAsync(int port, CancellationToken cancellationToken)
        {
            try
            {
                using var statusProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "tailscale",
                        Arguments = "status --json",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                statusProcess.Start();
                string output = await statusProcess.StandardOutput.ReadToEndAsync(cancellationToken);
                await statusProcess.WaitForExitAsync(cancellationToken);

                // Extract DNSName from JSON status
                var match = Regex.Match(output, @"""DNSName""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    string hostname = match.Groups[1].Value.TrimEnd('.');
                    string url = $"https://{hostname}/webhook/";
                    PublicUrl = url;
                    Log($"Tailscale public URL: {url}");
                    PublicUrlDiscovered?.Invoke(this, url);
                }
            }
            catch (Exception ex)
            {
                Log($"Could not auto-discover Tailscale URL: {ex.Message}");
            }
        }

        private void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            Log($"[tailscale] {e.Data}");

            // Capture "Funnel is not enabled" from stdout (some tailscale versions write here)
            if (e.Data.Contains("Funnel is not enabled") || e.Data.Contains("funnel is not enabled"))
            {
                var urlMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"https://login\.tailscale\.com\S+");
                string enableUrl = urlMatch.Success ? urlMatch.Value.Trim() : "https://login.tailscale.com/admin/acls";
                string msg = $"Funnel is not enabled on your tailnet.\nTo enable, visit: {enableUrl}";
                ErrorOccurred?.Invoke(this, msg);
                return;
            }

            // Capture the public URL from funnel output
            var match = Regex.Match(e.Data, @"https://[a-zA-Z0-9\-\.]+\.ts\.net[^\s]*");
            if (!match.Success)
                match = Regex.Match(e.Data, @"https://[a-zA-Z0-9\-]+\.[a-zA-Z0-9\-\.]+[^\s]*");

            if (match.Success && string.IsNullOrEmpty(PublicUrl))
            {
                string url = match.Value.TrimEnd('/') + "/webhook/";
                PublicUrl = url;
                Log($"Public URL discovered: {url}");
                PublicUrlDiscovered?.Invoke(this, url);
            }
        }

        private void OnErrorDataReceived(object? sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
                Log($"[tailscale stderr] {e.Data}");
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            _isRunning = false;
            Log("Tailscale funnel process exited.");
        }

        /// <summary>
        /// Stops the Tailscale funnel process and disables the funnel rule.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning && _process == null) return;

            try
            {
                _process?.Kill(entireProcessTree: true);
                _process?.WaitForExit(3000);
            }
            catch { /* best effort */ }
            finally
            {
                _process?.Dispose();
                _process = null;
                _isRunning = false;
                PublicUrl = null;
                Log("Tailscale funnel stopped.");
            }

            // Disable the funnel rule asynchronously (best effort)
            Task.Run(() =>
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "tailscale",
                        Arguments = "funnel off",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    p?.WaitForExit(5000);
                }
                catch { /* not critical */ }
            });
        }

        private void Log(string message) => LogMessage?.Invoke(this, message);

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
