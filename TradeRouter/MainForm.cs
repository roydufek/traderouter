using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TradeRouter.Models;

namespace TradeRouter
{
    public partial class MainForm : Form
    {
        // ── Core components ───────────────────────────────────────────────────
        private readonly NT8Client      _nt8            = new();
        private readonly WebhookServer  _webhookServer  = new();
        private readonly TailscaleFunnel _tailscale     = new();
        private readonly OrderMapper    _orderMapper    = new();
        private readonly SecurityManager _security      = new();
        private readonly IniFile        _ini;
        private readonly FileLogger     _logger;
        private bool                    _serverRunning  = false;

        private CancellationTokenSource? _connectCts;

        // ── Color palette ─────────────────────────────────────────────────────
        private static readonly Color BgDark     = Color.FromArgb(24, 26, 34);
        private static readonly Color BgPanel    = Color.FromArgb(36, 38, 48);
        private static readonly Color BgInput    = Color.FromArgb(50, 52, 64);
        private static readonly Color FgText     = Color.FromArgb(220, 220, 230);
        private static readonly Color FgMuted    = Color.FromArgb(130, 130, 150);
        private static readonly Color AccentBlue = Color.FromArgb(80, 140, 220);
        private static readonly Color GreenColor = Color.FromArgb(80, 200, 120);
        private static readonly Color RedColor   = Color.FromArgb(220, 70, 70);
        private static readonly Color AmberColor = Color.FromArgb(230, 170, 60);
        private static readonly Color GrayColor  = Color.FromArgb(100, 100, 115);

        public MainForm()
        {
            string baseDir = AppContext.BaseDirectory;
            _ini    = new IniFile(Path.Combine(baseDir, "TradeRouter.ini"));
            _logger = new FileLogger(baseDir);

            InitializeComponent();
            LoadIni();
            WireEvents();
            ApplyTheme();
            LoadLogo();

            _orderMapper.ResetPosition();
            UpdateNT8StatusDisplay(NT8Client.ConnectionState.Disconnected, string.Empty);

            Load += async (s, e) => await RunStartupSelfTestAsync();
        }

        // ── INI ───────────────────────────────────────────────────────────────

        private void LoadIni()
        {
            _ini.Load();
            EnsureIniDefaults();

            // NT8
            int nt8Port = _ini.GetInt("Connection", "NT8Port", 7091);
            nudNT8Port.Value = Math.Max(1024, Math.Min(65535, nt8Port));
            _nt8.SetPort(nt8Port);

            txtCopyPorts.Text = _ini.Get("Connection", "CopyPorts", "");

            // Webhook
            nudPort.Value = Math.Max(1024, Math.Min(65535, _ini.GetInt("Webhook", "Port", 7890)));
            nudRateLimit.Value = Math.Max(1, Math.Min(60, _ini.GetInt("Webhook", "RateLimit", 10)));
            _security.MaxOrdersPerMin = (int)nudRateLimit.Value;

            // Security
            string apiKey = _ini.Get("Security", "ApiKey", "");
            _security.ApiKey = apiKey;
            txtApiKey.Text   = apiKey;

            string ipMode = _ini.Get("Security", "IpMode", "Any");
            switch (ipMode)
            {
                case "TradingViewOnly": rbTvOnly.Checked   = true; _security.CurrentIpMode = SecurityManager.IpMode.TradingViewOnly; break;
                case "Custom":          rbCustomIp.Checked = true; _security.CurrentIpMode = SecurityManager.IpMode.Custom; break;
                default:                rbAnyIp.Checked    = true; _security.CurrentIpMode = SecurityManager.IpMode.Any; break;
            }

            string customIps = _ini.Get("Security", "CustomIps", "");
            txtCustomIps.Text = customIps;
            _security.SetCustomIps(customIps);
            txtCustomIps.Enabled = rbCustomIp.Checked;

            // Logging
            bool logToFile = _ini.GetBool("Logging", "LogToFile", false);
            chkLogToFile.Checked = logToFile;
            _logger.SetEnabled(logToFile);
        }

        private void EnsureIniDefaults()
        {
            bool dirty = false;
            void Ensure(string sec, string key, string def)
            {
                if (string.IsNullOrEmpty(_ini.Get(sec, key, ""))) { _ini.Set(sec, key, def); dirty = true; }
            }
            Ensure("Connection", "NT8Port",          "7091");
            Ensure("Connection", "CopyPorts",        "");
            Ensure("Connection", "NT8StrategyApiKey","");
            Ensure("Webhook",    "Port",             "7890");
            Ensure("Webhook",    "RateLimit",        "10");
            Ensure("Security",   "ApiKey",           "");
            Ensure("Security",   "IpMode",           "Any");
            Ensure("Security",   "CustomIps",        "");
            Ensure("Logging",    "LogToFile",        "false");
            if (dirty) _ini.Save();
        }

        private void SaveIni()
        {
            _ini.SetInt("Connection", "NT8Port",          (int)nudNT8Port.Value);
            _ini.Set(   "Connection", "CopyPorts",        txtCopyPorts.Text.Trim());
            _ini.SetInt("Webhook",    "Port",             (int)nudPort.Value);
            _ini.SetInt("Webhook",    "RateLimit",        (int)nudRateLimit.Value);
            _ini.Set(   "Security",   "ApiKey",           _security.ApiKey);
            _ini.Set(   "Security",   "IpMode",           GetIpModeString());
            _ini.Set(   "Security",   "CustomIps",        txtCustomIps.Text.Trim());
            _ini.SetBool("Logging",   "LogToFile",        chkLogToFile.Checked);
            _ini.Save();
        }

        private string GetIpModeString()
        {
            if (rbTvOnly.Checked)   return "TradingViewOnly";
            if (rbCustomIp.Checked) return "Custom";
            return "Any";
        }

        // ── Logo ──────────────────────────────────────────────────────────────

        private void LoadLogo()
        {
            try
            {
                string logoPath = Path.Combine(AppContext.BaseDirectory, "assets", "logo.png");
                if (!File.Exists(logoPath))
                    logoPath = Path.Combine(AppContext.BaseDirectory, "logo.png");
                if (File.Exists(logoPath))
                    picLogo.Image = Image.FromFile(logoPath);
            }
            catch { /* logo optional */ }
        }

        // ── Startup Self-Test ─────────────────────────────────────────────────

        private async Task RunStartupSelfTestAsync()
        {
            SetStatus("Self-test running...");
            _logger.Info("Startup self-test started.");

            // NT8 port reachable?
            bool nt8Ok = await Task.Run(() =>
            {
                try { using var tc = new TcpClient(); return tc.ConnectAsync("127.0.0.1", _nt8.Port).Wait(2000) && tc.Connected; }
                catch { return false; }
            });

            string nt8Msg = nt8Ok ? $"NT8:{_nt8.Port} ✓ reachable" : $"NT8:{_nt8.Port} ✗ not reachable (start WebhookOrderStrategy_v1_0_5 in NT8)";
            AppendConsole(nt8Msg, nt8Ok ? GreenColor : AmberColor);
            _logger.Info($"Self-test: {nt8Msg}");

            // Tailscale?
            bool tsOk = await Task.Run(() => TailscaleFunnel.IsTailscaleAvailable());
            string tsMsg = tsOk ? "tailscale ✓ found" : "tailscale ✗ not found in PATH";
            AppendConsole(tsMsg, tsOk ? GreenColor : FgMuted);
            if (!tsOk) { chkTailscale.Enabled = false; chkTailscale.Text = "Tailscale (not found)"; }

            // Firewall check (passive — info only)
            int webhookPort = (int)nudPort.Value;
            bool fwOk = await Task.Run(() => IsFirewallRulePresent(webhookPort));
            AppendConsole(fwOk
                ? $"Firewall: ✓ rule exists for port {webhookPort}."
                : $"Firewall: ✗ no rule for port {webhookPort} — click 'Fix Firewall' before starting.",
                fwOk ? GreenColor : AmberColor);

            SetStatus($"Ready.  NT8:{(nt8Ok ? "✓" : "✗")}  Tailscale:{(tsOk ? "✓" : "✗")}  Firewall:{(fwOk ? "✓" : "✗")}");
            _logger.Info("Startup self-test complete.");
        }

        // ── Wire Events ───────────────────────────────────────────────────────

        private void WireEvents()
        {
            _webhookServer.Security = _security;

            _nt8.StateChanged += (s, state) => SafeInvoke(() => UpdateNT8StatusDisplay(state, _nt8.FailReason));
            _nt8.LogMessage   += (s, msg)   => SafeInvoke(() => { AppendConsole($"[NT8] {msg}"); _logger.Info($"NT8: {msg}"); });

            _webhookServer.WebhookReceived += OnWebhookReceived;
            _webhookServer.LogMessage  += (s, msg) => SafeInvoke(() => { AppendConsole($"[WH] {msg}"); _logger.Info($"WH: {msg}"); });
            _webhookServer.ErrorOccurred += (s, ex) => SafeInvoke(() => { AppendConsole($"[WH ERROR] {ex.Message}", RedColor); SetStatus($"Webhook error: {ex.Message}"); });

            _tailscale.LogMessage        += (s, msg) => SafeInvoke(() => AppendConsole($"[TS] {msg}"));
            _tailscale.PublicUrlDiscovered += (s, url) => SafeInvoke(() => { lblWebhookUrl.Text = url; AppendConsole($"[TS] URL: {url}", GreenColor); });
            _tailscale.ErrorOccurred += (s, msg) => SafeInvoke(async () =>
            {
                AppendConsole($"[TS ERROR] {msg}", RedColor);
                if (msg.Contains("Funnel is not enabled"))
                {
                    AppendConsole("✗ Stopping server — fix Tailscale Funnel access then restart.", RedColor);
                    await Task.Run(() => _webhookServer.Stop());
                    await Task.Run(() => _tailscale.Stop());
                    btnStartStop.Text = "Start Server";
                    SetServerRunningUi(false);
                    SetStatus("Stopped — Tailscale Funnel not enabled.");
                }
                else
                {
                    lblWebhookUrl.Text = $"Error: {msg}";
                }
            });

            chkTailscale.CheckedChanged  += chkTailscale_CheckedChanged;
            FormClosing += OnFormClosing;
        }

        // ── NT8 Connection ────────────────────────────────────────────────────

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (_nt8.IsConnected)
            {
                _nt8.Disconnect();
                btnConnect.Text = "Connect";
                UpdateNT8StatusDisplay(NT8Client.ConnectionState.Disconnected, "");
                SetStatus("Disconnected from NT8 strategy.");
                _logger.Info("Disconnected by user.");
                return;
            }

            btnConnect.Enabled = false;
            btnConnect.Text    = "Connecting...";
            _connectCts        = new CancellationTokenSource();

            try
            {
                await _nt8.ConnectAndGetAccountsAsync(_connectCts.Token);
                AppendConsole($"✓ Connected to NT8 strategy on port {_nt8.Port}.", GreenColor);
                SetStatus($"Connected — NT8 strategy port {_nt8.Port}.");
                btnConnect.Text = "Disconnect";
            }
            catch (OperationCanceledException)
            {
                AppendConsole("Connection cancelled.");
                btnConnect.Text = "Connect";
            }
            catch (Exception ex)
            {
                AppendConsole($"✗ NT8 connect failed: {ex.Message}", RedColor);
                SetStatus($"NT8 connection failed.");
                _logger.Error($"NT8 connect: {ex.Message}");
                MessageBox.Show(
                    $"Could not reach NT8 strategy on port {_nt8.Port}.\n\n{ex.Message}\n\n" +
                    $"Make sure WebhookOrderStrategy_v1_0_5 is loaded in NT8 and listening on port {_nt8.Port}.",
                    "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnConnect.Text = "Connect";
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private void nudNT8Port_ValueChanged(object? sender, EventArgs e)
        {
            int port = (int)nudNT8Port.Value;
            _nt8.SetPort(port);
            _ini.SetInt("Connection", "NT8Port", port);
            _ini.Save();
        }

        private void txtCopyPorts_TextChanged(object? sender, EventArgs e)
        {
            _ini.Set("Connection", "CopyPorts", txtCopyPorts.Text.Trim());
            _ini.Save();
        }

        // ── Port Registration ─────────────────────────────────────────────────

        private List<int> GetNT8Ports()
        {
            // NT8 strategy ports only — webhook port does NOT need urlacl registration
            var ports = new List<int> { (int)nudNT8Port.Value };
            foreach (string s in txtCopyPorts.Text.Split(','))
            {
                if (int.TryParse(s.Trim(), out int p) && p > 1023 && p <= 65535)
                    ports.Add(p);
            }
            return ports;
        }

        private async void btnRegisterPorts_Click(object sender, EventArgs e)
        {
            var ports = GetNT8Ports();
            btnRegisterPorts.Enabled = false;
            AppendConsole($"Registering NT8 ports: {string.Join(", ", ports)}");

            foreach (int port in ports)
            {
                try
                {
                    // Register both http://+: and http://localhost: — both required on some Windows configs
                    foreach (string prefix in new[] { $"http://+:{port}/", $"http://localhost:{port}/" })
                    {
                        AppendConsole($"  Registering {prefix}...");
                        var psi = new ProcessStartInfo
                        {
                            FileName        = "netsh",
                            Arguments       = $"http add urlacl url={prefix} user=Everyone",
                            Verb            = "runas",
                            UseShellExecute = true,
                            CreateNoWindow  = true
                        };
                        var proc = Process.Start(psi);
                        await Task.Run(() => proc?.WaitForExit(10000));
                    }
                    AppendConsole($"  Port {port}: ✓ done", GreenColor);
                }
                catch (Exception ex)
                {
                    AppendConsole($"  Port {port}: failed — {ex.Message}", RedColor);
                }
            }

            AppendConsole("Done. Reload WebhookOrderStrategy_v1_0_5 in NT8 (right-click → Reload).", AmberColor);
            btnRegisterPorts.Enabled = true;
        }

        private async void btnFixFirewall_Click(object sender, EventArgs e)
        {
            int port = (int)nudPort.Value;
            btnFixFirewall.Enabled = false;

            bool alreadyOk = await Task.Run(() => IsFirewallRulePresent(port));
            if (alreadyOk)
            {
                AppendConsole($"✓ Firewall rule for port {port} already exists.", GreenColor);
                btnFixFirewall.Enabled = true;
                return;
            }

            var result = MessageBox.Show(
                $"No Windows Firewall inbound rule found for port {port}.\n\n" +
                "This is needed for TradeRouter to receive webhooks from TradingView/Tailscale.\n\n" +
                "Click Yes to add the rule now (a UAC admin prompt will appear).",
                "Fix Firewall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            { AppendConsole("Firewall fix skipped."); btnFixFirewall.Enabled = true; return; }

            try
            {
                AppendConsole($"Adding firewall rule for port {port}...");
                var psi = new ProcessStartInfo
                {
                    FileName        = "netsh",
                    Arguments       = $"advfirewall firewall add rule name=\"TradeRouter Port {port}\" " +
                                      $"dir=in action=allow protocol=TCP localport={port}",
                    Verb            = "runas",
                    UseShellExecute = true,
                    CreateNoWindow  = true
                };
                var proc = Process.Start(psi);
                await Task.Run(() => proc?.WaitForExit(10000));

                bool ok = await Task.Run(() => IsFirewallRulePresent(port));
                AppendConsole($"Port {port} firewall: {(ok ? "✓ rule added successfully" : "✗ still not found — try manually")}", ok ? GreenColor : RedColor);
            }
            catch (Exception ex)
            {
                AppendConsole($"Firewall fix failed: {ex.Message}", RedColor);
            }
            btnFixFirewall.Enabled = true;
        }

        private static bool IsFirewallRulePresent(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "netsh",
                    Arguments              = $"advfirewall firewall show rule name=\"TradeRouter Port {port}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                var proc = Process.Start(psi)!;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return output.Contains($"{port}");
            }
            catch { return false; }
        }

        // ── Emergency Flatten ────────────────────────────────────────────────

        private async void btnEmergencyFlatten_Click(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(
                "Send EMERGENCY FLATTEN to all configured NT8 strategy ports?\n\nThis will attempt to close all open positions immediately.",
                "Emergency Flatten", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            string flattenJson = $"{{\"action\":\"flatten\",\"sentiment\":\"flat\",\"quantity\":\"0\",\"price\":\"0\",\"time\":\"{DateTime.UtcNow:O}\"}}";
            string? nt8ApiKey  = _ini.Get("Connection", "NT8StrategyApiKey", "");
            if (string.IsNullOrWhiteSpace(nt8ApiKey)) nt8ApiKey = null;

            AppendConsole("⚠ EMERGENCY FLATTEN — sending to all ports...", RedColor);
            _logger.Warn("Emergency flatten triggered by user.");

            var ports = GetNT8Ports();
            var tasks = new List<Task>();

            foreach (int port in ports)
            {
                int p = port;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var client  = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                        using var content = new StringContent(flattenJson, Encoding.UTF8, "application/json");
                        if (!string.IsNullOrWhiteSpace(nt8ApiKey))
                            content.Headers.Add("X-Api-Key", nt8ApiKey);
                        var resp = await client.PostAsync($"http://127.0.0.1:{p}/", content);
                        string body = await resp.Content.ReadAsStringAsync();
                        SafeInvoke(() => AppendConsole($"  ← NT8:{p} {(int)resp.StatusCode}: {body}", resp.IsSuccessStatusCode ? GreenColor : RedColor));
                    }
                    catch (Exception ex)
                    {
                        SafeInvoke(() => AppendConsole($"  ✗ NT8:{p} flatten failed: {ex.Message}", RedColor));
                    }
                }));
            }

            await Task.WhenAll(tasks);
            AppendConsole("Emergency flatten sent to all ports.", AmberColor);
            _logger.Warn("Emergency flatten sent.");
        }

        // ── Webhook Server ────────────────────────────────────────────────────

        private async void btnStartStop_Click(object sender, EventArgs e)
        {
            if (_webhookServer.IsRunning)
            {
                // Disable button while stopping to prevent double-clicks / freeze perception
                btnStartStop.Enabled = false;
                btnStartStop.Text    = "Stopping...";
                SetStatus("Stopping server...");

                await Task.Run(() =>
                {
                    _webhookServer.Stop();
                    _tailscale.Stop();     // WaitForExit is inside Stop() — keep it off the UI thread
                });

                btnStartStop.Text    = "Start Server";
                btnStartStop.Enabled = true;
                lblWebhookUrl.Text   = "(server stopped)";
                SetServerRunningUi(false);
                AppendConsole("Webhook server stopped.");
                SetStatus("Server stopped.");
                return;
            }

            int port = (int)nudPort.Value;
            btnStartStop.Enabled = false;
            btnStartStop.Text    = "Starting...";

            // Firewall check before starting
            bool fwReady = await Task.Run(() => IsFirewallRulePresent(port));
            if (!fwReady)
            {
                var fwResult = MessageBox.Show(
                    $"No Windows Firewall inbound rule found for port {port}.\n\n" +
                    "Without this, inbound webhooks from TradingView/Tailscale may be blocked.\n\n" +
                    "Click Yes to add the rule now (UAC prompt will appear).\n" +
                    "Click No to skip and accept the risk.",
                    "Firewall Rule Missing",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (fwResult == DialogResult.Yes)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName        = "netsh",
                            Arguments       = $"advfirewall firewall add rule name=\"TradeRouter Port {port}\" " +
                                              $"dir=in action=allow protocol=TCP localport={port}",
                            Verb            = "runas",
                            UseShellExecute = true,
                            CreateNoWindow  = true
                        };
                        var proc = Process.Start(psi);
                        await Task.Run(() => proc?.WaitForExit(10000));
                        bool ok = await Task.Run(() => IsFirewallRulePresent(port));
                        AppendConsole(ok ? $"✓ Firewall rule added for port {port}." : $"⚠ Firewall rule may not have been added — proceeding anyway.", ok ? GreenColor : AmberColor);
                    }
                    catch (Exception fwEx)
                    {
                        AppendConsole($"⚠ Firewall rule failed: {fwEx.Message} — proceeding anyway.", AmberColor);
                    }
                }
                else
                {
                    AppendConsole("⚠ Firewall rule skipped — inbound webhooks may not reach this port.", AmberColor);
                }
            }

            try
            {
                _webhookServer.Start(port);
                btnStartStop.Text  = "Stop Server";
                lblWebhookUrl.Text = $"http://localhost:{port}/webhook/";
                SetServerRunningUi(true);
                AppendConsole($"✓ Webhook server started on port {port}.", GreenColor);
                SetStatus($"Listening on port {port}.");

                if (chkTailscale.Checked)
                {
                    AppendConsole("Starting Tailscale Funnel...");
                    await _tailscale.StartAsync(port);
                }
            }
            catch (Exception ex)
            {
                AppendConsole($"✗ Failed to start server: {ex.Message}", RedColor);
                SetStatus("Server start failed.");
                btnStartStop.Text = "Start Server";
                SetServerRunningUi(false);
                MessageBox.Show($"Failed to start webhook server on port {port}:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnStartStop.Enabled = true;
            }
        }

        /// Lock/unlock controls that shouldn't change while server is running.
        private void SetServerRunningUi(bool running)
        {
            nudPort.Enabled        = !running;
            btnFixFirewall.Enabled = !running;

            // Don't use Enabled=false on CheckBox — WinForms overrides ForeColor with
            // system gray, which looks broken on dark theme. Lock it via event guard instead.
            chkTailscale.ForeColor = running ? FgMuted : FgText;
            _serverRunning = running;
        }

        private void chkTailscale_CheckedChanged(object? sender, EventArgs e)
        {
            // Ignore while server is running — checkbox is visually muted but still clickable
            if (_serverRunning)
            {
                chkTailscale.CheckedChanged -= chkTailscale_CheckedChanged;
                chkTailscale.Checked = !chkTailscale.Checked; // revert
                chkTailscale.CheckedChanged += chkTailscale_CheckedChanged;
                return;
            }
            if (!_tailscale.IsRunning) return;
            Task.Run(() => _tailscale.Stop());
            lblWebhookUrl.Text = $"http://localhost:{nudPort.Value}/webhook/";
        }

        // ── Webhook Processing ────────────────────────────────────────────────

        private void OnWebhookReceived(object? sender, WebhookPayload payload)
        {
            SafeInvoke(async () =>
            {
                string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
                AppendConsole($"→ Webhook: {json}");
                await ProcessWebhookAsync(payload);
            });
        }

        private async Task ProcessWebhookAsync(WebhookPayload payload)
        {
            if (!_nt8.IsConnected)
            {
                AppendConsole("⚠ NT8 not connected — order dropped.", AmberColor);
                SetStatus("⚠ NT8 not connected — order dropped!");
                _logger.Warn("Order dropped: NT8 not connected.");
                return;
            }

            // Validate
            TradeOrder? order = _orderMapper.Map(payload, "NT8Strategy");
            if (order == null)
            {
                AppendConsole($"⚠ Unmappable: action={payload.Action} sentiment={payload.Sentiment} — skipped.", AmberColor);
                _logger.Warn($"Unmappable: action={payload.Action} sentiment={payload.Sentiment}");
                return;
            }

            string forwardJson = payload.ToForwardJson();
            AppendConsole($"► Forwarding: {forwardJson}");
            _logger.Info($"Forwarding: {forwardJson}");

            string? nt8ApiKey = _ini.Get("Connection", "NT8StrategyApiKey", "");
            if (string.IsNullOrWhiteSpace(nt8ApiKey)) nt8ApiKey = null;

            // Primary port
            try
            {
                string response = await _nt8.SendOrderAsync(payload, nt8ApiKey);
                AppendConsole($"◀ NT8:{_nt8.Port} → {response.Trim()}", GreenColor);
                SetStatus($"Order forwarded: {payload}");
                _logger.Info($"NT8:{_nt8.Port} response: {response.Trim()}");
            }
            catch (Exception ex)
            {
                AppendConsole($"✗ NT8:{_nt8.Port} error: {ex.Message}", RedColor);
                SetStatus($"NT8 error: {ex.Message}");
                _logger.Error($"NT8 forward error: {ex.Message}");
            }

            // Copy ports (fire-and-forget)
            foreach (string s in txtCopyPorts.Text.Split(','))
            {
                if (!int.TryParse(s.Trim(), out int copyPort) || copyPort < 1024) continue;
                int p = copyPort;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var client  = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                        using var content = new StringContent(forwardJson, Encoding.UTF8, "application/json");
                        if (!string.IsNullOrWhiteSpace(nt8ApiKey)) content.Headers.Add("X-Api-Key", nt8ApiKey);
                        var resp = await client.PostAsync($"http://127.0.0.1:{p}/", content);
                        string body = await resp.Content.ReadAsStringAsync();
                        SafeInvoke(() => AppendConsole($"◀ NT8:{p} → {body.Trim()}", resp.IsSuccessStatusCode ? GreenColor : RedColor));
                        _logger.Info($"NT8:{p} copy response: {body.Trim()}");
                    }
                    catch (Exception ex)
                    {
                        SafeInvoke(() => AppendConsole($"✗ NT8:{p} copy error: {ex.Message}", RedColor));
                        _logger.Error($"NT8:{p} copy: {ex.Message}");
                    }
                });
            }
        }

        // ── Security controls ─────────────────────────────────────────────────

        private void btnGenerateKey_Click(object? sender, EventArgs e)
        {
            string key = SecurityManager.GenerateApiKey();
            _security.ApiKey = key;
            txtApiKey.Text   = key;
            SaveIni();
            AppendConsole("API key generated. Add to your TradingView payload as \"api\": \"KEY\".", GreenColor);
            SetStatus("API key generated.");
        }

        private void btnClearKey_Click(object? sender, EventArgs e)
        {
            _security.ApiKey = "";
            txtApiKey.Text   = "";
            SaveIni();
            AppendConsole("API key cleared — auth disabled.");
            SetStatus("API key cleared.");
        }

        private void rbIpMode_CheckedChanged(object? sender, EventArgs e)
        {
            _security.CurrentIpMode = rbTvOnly.Checked   ? SecurityManager.IpMode.TradingViewOnly
                                    : rbCustomIp.Checked ? SecurityManager.IpMode.Custom
                                    : SecurityManager.IpMode.Any;
            txtCustomIps.Enabled = rbCustomIp.Checked;
            SaveIni();
        }

        private void txtCustomIps_TextChanged(object? sender, EventArgs e)
        {
            _security.SetCustomIps(txtCustomIps.Text);
            SaveIni();
        }

        private void nudRateLimit_ValueChanged(object? sender, EventArgs e)
        {
            _security.MaxOrdersPerMin = (int)nudRateLimit.Value;
            SaveIni();
        }

        private void nudPort_ValueChanged(object? sender, EventArgs e) => SaveIni();

        private void chkLogToFile_CheckedChanged(object? sender, EventArgs e)
        {
            _logger.SetEnabled(chkLogToFile.Checked);
            SaveIni();
        }

        // ── Copy Payload / URL ────────────────────────────────────────────────

        private void btnCopyPayload_Click(object? sender, EventArgs e)
        {
            string key    = _security.ApiKey;
            bool   hasKey = !string.IsNullOrWhiteSpace(key);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            if (hasKey) sb.AppendLine($"  \"api\": \"{key}\",");
            sb.AppendLine("  \"action\": \"{{strategy.order.action}}\",");
            sb.AppendLine("  \"sentiment\": \"{{strategy.market_position}}\",");
            sb.AppendLine("  \"quantity\": \"{{strategy.order.contracts}}\",");
            sb.AppendLine("  \"price\": \"{{close}}\",");
            sb.AppendLine("  \"time\": \"{{timenow}}\"");
            sb.Append("}");

            Clipboard.SetText(sb.ToString());
            SetStatus("Payload JSON copied to clipboard.");
            AppendConsole("Payload JSON copied to clipboard.", GreenColor);
        }

        private void btnCopyUrl_Click(object? sender, EventArgs e)
        {
            string url = lblWebhookUrl.Text;
            if (!url.StartsWith("http")) { SetStatus("Server not running — no URL to copy."); return; }
            Clipboard.SetText(url);
            SetStatus("Webhook URL copied.");
        }

        // ── NT8 Status Display ────────────────────────────────────────────────

        private void UpdateNT8StatusDisplay(NT8Client.ConnectionState state, string failReason)
        {
            (Color dot, string label) = state switch
            {
                NT8Client.ConnectionState.Connected    => (GreenColor, "Connected"),
                NT8Client.ConnectionState.Connecting   => (AmberColor, "Connecting..."),
                NT8Client.ConnectionState.Failed       => (RedColor,   $"Failed: {failReason}"),
                _ => (GrayColor, "Disconnected")
            };

            pnlStatusDot.BackColor = dot;
            lblNT8Status.Text      = label;
            lblNT8Status.ForeColor = dot;
        }

        // ── Console ───────────────────────────────────────────────────────────

        private void AppendConsole(string message, Color? color = null)
        {
            if (rtbConsole.InvokeRequired)
            { rtbConsole.Invoke(() => AppendConsole(message, color)); return; }

            // Keep max 2000 lines
            if (rtbConsole.Lines.Length > 2000)
                rtbConsole.Select(0, rtbConsole.GetFirstCharIndexFromLine(500));
            rtbConsole.SelectionStart  = rtbConsole.TextLength;
            rtbConsole.SelectionLength = 0;
            rtbConsole.SelectionColor  = color ?? FgText;
            rtbConsole.AppendText($"[{Timestamp()}] {message}{Environment.NewLine}");
            rtbConsole.ScrollToCaret();
        }

        // Legacy shims for any code that still calls the old log methods
        private void AppendWebhookLog(string msg) => AppendConsole(msg);
        private void AppendNT8Log(string msg)      => AppendConsole(msg);

        // ── Theme ─────────────────────────────────────────────────────────────

        private void ApplyTheme()
        {
            BackColor = BgDark;
            ForeColor = FgText;

            void Style(System.Windows.Forms.Control ctrl)
            {
                // Determine the "panel" background for this control's parent
                Color parentBg = ctrl.Parent is GroupBox ? BgPanel : BgDark;

                if (ctrl is GroupBox gb)
                {
                    gb.ForeColor = FgMuted;
                    gb.BackColor = BgPanel;
                }
                else if (ctrl is Button btn)
                {
                    btn.BackColor = BgInput;
                    btn.ForeColor = FgText;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 85);
                }
                else if (ctrl is TextBox tb)
                {
                    tb.BackColor   = BgInput;
                    tb.ForeColor   = FgText;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (ctrl is NumericUpDown nud)
                {
                    nud.BackColor = BgInput;
                    nud.ForeColor = FgText;
                }
                else if (ctrl is ComboBox cb)
                {
                    cb.BackColor = BgInput;
                    cb.ForeColor = FgText;
                }
                else if (ctrl is CheckBox chk)
                {
                    chk.BackColor = parentBg;   // no Transparent — WinForms limitation
                    chk.ForeColor = FgText;
                }
                else if (ctrl is RadioButton rb)
                {
                    rb.BackColor = parentBg;    // no Transparent
                    rb.ForeColor = FgText;
                }
                else if (ctrl is Label lbl)
                {
                    lbl.BackColor = parentBg;   // no Transparent
                    lbl.ForeColor = FgText;
                }
                else if (ctrl is RichTextBox rtb)
                {
                    rtb.BackColor = BgDark;
                    rtb.ForeColor = FgText;
                }
                else if (ctrl is Panel pnl)
                {
                    // Leave panels alone — they get BackColor set explicitly (e.g. status dot)
                }
                else if (ctrl is PictureBox)
                {
                    // PictureBox: set to actual parent bg, not Transparent
                    ctrl.BackColor = parentBg;
                }
                else
                {
                    ctrl.BackColor = parentBg;
                    ctrl.ForeColor = FgText;
                }

                foreach (System.Windows.Forms.Control child in ctrl.Controls) Style(child);
            }

            foreach (System.Windows.Forms.Control ctrl in Controls) Style(ctrl);

            // Emergency flatten — red
            btnEmergencyFlatten.BackColor = Color.FromArgb(160, 30, 30);
            btnEmergencyFlatten.ForeColor = Color.White;
            btnEmergencyFlatten.FlatAppearance.BorderColor = Color.FromArgb(200, 50, 50);

            statusStrip.BackColor = Color.FromArgb(20, 20, 28);
            statusStrip.ForeColor = FgMuted;
            tsslStatus.ForeColor  = FgMuted;
        }

        // ── Status Bar ────────────────────────────────────────────────────────

        private void SetStatus(string message)
        {
            if (statusStrip.InvokeRequired) { statusStrip.Invoke(() => SetStatus(message)); return; }
            tsslStatus.Text = message;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss.fff");

        private static string GetSelectedInstrument() => "YM 06-26";

        private void SafeInvoke(Action action)
        {
            if (InvokeRequired) Invoke(action);
            else action();
        }

        private async Task SafeInvoke_Async(Func<Task> action)
        {
            if (InvokeRequired) await (Task)Invoke(action);
            else await action();
        }

        // ── Form Closing ──────────────────────────────────────────────────────

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            SaveIni();
            _webhookServer.Stop();
            Task.Run(() => _tailscale.Stop()).Wait(4000);   // off UI thread, bounded wait
            _nt8.Disconnect();
            _nt8.Dispose();
            _logger.Info("TradeRouter closed.");
        }
    }
}
