namespace TradeRouter
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        // ── NT8 Connection ───────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox  grpNT8;
        private System.Windows.Forms.Panel     pnlStatusDot;
        private System.Windows.Forms.Label     lblNT8Status;
        private System.Windows.Forms.Label     lblNT8PortLabel;
        private System.Windows.Forms.NumericUpDown nudNT8Port;
        private System.Windows.Forms.Button    btnConnect;
        private System.Windows.Forms.Label     lblCopyPortsLabel;
        private System.Windows.Forms.TextBox   txtCopyPorts;
        private System.Windows.Forms.Button    btnRegisterPorts;
        private System.Windows.Forms.Button    btnEmergencyFlatten;
        private System.Windows.Forms.PictureBox picLogo;

        // ── Webhook Server ───────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox  grpWebhook;
        private System.Windows.Forms.Label     lblPortLabel;
        private System.Windows.Forms.NumericUpDown nudPort;
        private System.Windows.Forms.Button    btnStartStop;
        private System.Windows.Forms.Button    btnFixFirewall;
        private System.Windows.Forms.CheckBox  chkTailscale;
        private System.Windows.Forms.Label     lblUrlLabel;
        private System.Windows.Forms.Label     lblWebhookUrl;
        private System.Windows.Forms.Button    btnCopyUrl;
        private System.Windows.Forms.Button    btnCopyPayload;

        // ── Security ─────────────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox  grpSecurity;
        private System.Windows.Forms.Label     lblApiKeyLabel;
        private System.Windows.Forms.TextBox   txtApiKey;
        private System.Windows.Forms.Button    btnGenerateKey;
        private System.Windows.Forms.Button    btnClearKey;
        private System.Windows.Forms.Label     lblIpModeLabel;
        private System.Windows.Forms.RadioButton rbAnyIp;
        private System.Windows.Forms.RadioButton rbTvOnly;
        private System.Windows.Forms.RadioButton rbCustomIp;
        private System.Windows.Forms.TextBox   txtCustomIps;
        private System.Windows.Forms.Label     lblRateLimitLabel;
        private System.Windows.Forms.NumericUpDown nudRateLimit;
        private System.Windows.Forms.CheckBox  chkLogToFile;

        // ── Console ──────────────────────────────────────────────────────────
        private System.Windows.Forms.GroupBox  grpConsole;
        private System.Windows.Forms.RichTextBox rtbConsole;
        private System.Windows.Forms.Button    btnClearConsole;

        // ── Status strip ─────────────────────────────────────────────────────
        private System.Windows.Forms.StatusStrip     statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel tsslStatus;

        // Stubs kept for INI compatibility (not shown in UI)
        private System.Windows.Forms.ComboBox  cboAccount   = new System.Windows.Forms.ComboBox();
        private System.Windows.Forms.CheckBox  chkAutoSync  = new System.Windows.Forms.CheckBox();
        private System.Windows.Forms.NumericUpDown nudSyncInterval = new System.Windows.Forms.NumericUpDown();

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            SuspendLayout();

            // ── Instantiate ────────────────────────────────────────────────
            grpNT8              = new System.Windows.Forms.GroupBox();
            grpWebhook          = new System.Windows.Forms.GroupBox();
            grpSecurity         = new System.Windows.Forms.GroupBox();
            grpConsole          = new System.Windows.Forms.GroupBox();

            pnlStatusDot        = new System.Windows.Forms.Panel();
            lblNT8Status        = new System.Windows.Forms.Label();
            lblNT8PortLabel     = new System.Windows.Forms.Label();
            nudNT8Port          = new System.Windows.Forms.NumericUpDown();
            btnConnect          = new System.Windows.Forms.Button();
            lblCopyPortsLabel   = new System.Windows.Forms.Label();
            txtCopyPorts        = new System.Windows.Forms.TextBox();
            btnRegisterPorts    = new System.Windows.Forms.Button();
            btnEmergencyFlatten = new System.Windows.Forms.Button();
            picLogo             = new System.Windows.Forms.PictureBox();

            lblPortLabel        = new System.Windows.Forms.Label();
            nudPort             = new System.Windows.Forms.NumericUpDown();
            btnStartStop        = new System.Windows.Forms.Button();
            btnFixFirewall      = new System.Windows.Forms.Button();
            chkTailscale        = new System.Windows.Forms.CheckBox();
            lblUrlLabel         = new System.Windows.Forms.Label();
            lblWebhookUrl       = new System.Windows.Forms.Label();
            btnCopyUrl          = new System.Windows.Forms.Button();
            btnCopyPayload      = new System.Windows.Forms.Button();

            lblApiKeyLabel      = new System.Windows.Forms.Label();
            txtApiKey           = new System.Windows.Forms.TextBox();
            btnGenerateKey      = new System.Windows.Forms.Button();
            btnClearKey         = new System.Windows.Forms.Button();
            lblIpModeLabel      = new System.Windows.Forms.Label();
            rbAnyIp             = new System.Windows.Forms.RadioButton();
            rbTvOnly            = new System.Windows.Forms.RadioButton();
            rbCustomIp          = new System.Windows.Forms.RadioButton();
            txtCustomIps        = new System.Windows.Forms.TextBox();
            lblRateLimitLabel   = new System.Windows.Forms.Label();
            nudRateLimit        = new System.Windows.Forms.NumericUpDown();
            chkLogToFile        = new System.Windows.Forms.CheckBox();

            rtbConsole          = new System.Windows.Forms.RichTextBox();
            btnClearConsole     = new System.Windows.Forms.Button();

            statusStrip         = new System.Windows.Forms.StatusStrip();
            tsslStatus          = new System.Windows.Forms.ToolStripStatusLabel();

            // ── Form ──────────────────────────────────────────────────────
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize          = new System.Drawing.Size(1100, 780);
            MinimumSize         = new System.Drawing.Size(900, 640);
            Text                = $"TradeRouter v{Application.ProductVersion} — TradingView → NinjaTrader 8";
            Font                = new System.Drawing.Font("Segoe UI", 9F);
            StartPosition       = System.Windows.Forms.FormStartPosition.CenterScreen;

            // ── Logo (top-right) ──────────────────────────────────────────
            picLogo.Size        = new System.Drawing.Size(64, 64);
            picLogo.Location    = new System.Drawing.Point(1024, 8);
            picLogo.Anchor      = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            picLogo.SizeMode    = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            picLogo.BackColor   = System.Drawing.Color.FromArgb(24, 26, 34);

            // ── grpNT8 (top-left, 680×150) ────────────────────────────────
            grpNT8.Text     = "NT8 Strategy Connection";
            grpNT8.Location = new System.Drawing.Point(12, 12);
            grpNT8.Size     = new System.Drawing.Size(1000, 150);
            grpNT8.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            // Status dot + label
            pnlStatusDot.Size     = new System.Drawing.Size(16, 16);
            pnlStatusDot.Location = new System.Drawing.Point(12, 32);

            lblNT8Status.Text      = "Disconnected";
            lblNT8Status.Location  = new System.Drawing.Point(34, 30);
            lblNT8Status.Size      = new System.Drawing.Size(160, 20);
            lblNT8Status.AutoSize  = false;

            // Port field
            lblNT8PortLabel.Text      = "Strategy Port:";
            lblNT8PortLabel.Location  = new System.Drawing.Point(210, 30);
            lblNT8PortLabel.Size      = new System.Drawing.Size(85, 20);
            lblNT8PortLabel.AutoSize  = false;
            lblNT8PortLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            nudNT8Port.Location  = new System.Drawing.Point(298, 28);
            nudNT8Port.Size      = new System.Drawing.Size(75, 23);
            nudNT8Port.Minimum   = 1024;
            nudNT8Port.Maximum   = 65535;
            nudNT8Port.Value     = 7091;
            nudNT8Port.ValueChanged += nudNT8Port_ValueChanged;

            btnConnect.Text     = "Connect";
            btnConnect.Location = new System.Drawing.Point(384, 26);
            btnConnect.Size     = new System.Drawing.Size(88, 28);
            btnConnect.Click   += btnConnect_Click;

            // Copy ports
            lblCopyPortsLabel.Text      = "Copy Ports:";
            lblCopyPortsLabel.Location  = new System.Drawing.Point(12, 72);
            lblCopyPortsLabel.Size      = new System.Drawing.Size(76, 20);
            lblCopyPortsLabel.AutoSize  = false;
            lblCopyPortsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            txtCopyPorts.Location     = new System.Drawing.Point(92, 70);
            txtCopyPorts.Size         = new System.Drawing.Size(200, 23);
            txtCopyPorts.PlaceholderText = "e.g. 7092, 7093  (comma-separated)";
            txtCopyPorts.TextChanged += txtCopyPorts_TextChanged;

            btnRegisterPorts.Text     = "Register Ports";
            btnRegisterPorts.Location = new System.Drawing.Point(304, 68);
            btnRegisterPorts.Size     = new System.Drawing.Size(168, 26);
            btnRegisterPorts.Click   += btnRegisterPorts_Click;

            // Emergency Flatten
            btnEmergencyFlatten.Text      = "⚠ EMERGENCY FLATTEN ALL";
            btnEmergencyFlatten.Location  = new System.Drawing.Point(12, 108);
            btnEmergencyFlatten.Size      = new System.Drawing.Size(460, 32);
            btnEmergencyFlatten.Font      = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            btnEmergencyFlatten.Click    += btnEmergencyFlatten_Click;

            grpNT8.Controls.AddRange(new System.Windows.Forms.Control[]
            {
                pnlStatusDot, lblNT8Status,
                lblNT8PortLabel, nudNT8Port, btnConnect,
                lblCopyPortsLabel, txtCopyPorts, btnRegisterPorts,
                btnEmergencyFlatten
            });

            // ── grpWebhook (top, below NT8, full-width) ────────────────────
            grpWebhook.Text     = "Webhook Server (TradingView Inbound)";
            grpWebhook.Location = new System.Drawing.Point(12, 174);
            grpWebhook.Size     = new System.Drawing.Size(1074, 90);
            grpWebhook.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            lblPortLabel.Text      = "Listen Port:";
            lblPortLabel.Location  = new System.Drawing.Point(12, 30);
            lblPortLabel.Size      = new System.Drawing.Size(76, 20);
            lblPortLabel.AutoSize  = false;
            lblPortLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            nudPort.Location  = new System.Drawing.Point(92, 28);
            nudPort.Size      = new System.Drawing.Size(75, 23);
            nudPort.Minimum   = 1024;
            nudPort.Maximum   = 65535;
            nudPort.Value     = 7890;
            nudPort.ValueChanged += nudPort_ValueChanged;

            btnStartStop.Text     = "Start Server";
            btnStartStop.Location = new System.Drawing.Point(178, 26);
            btnStartStop.Size     = new System.Drawing.Size(100, 28);
            btnStartStop.Click   += btnStartStop_Click;

            btnFixFirewall.Text      = "Fix Firewall";
            btnFixFirewall.Location  = new System.Drawing.Point(284, 26);
            btnFixFirewall.Size      = new System.Drawing.Size(96, 28);
            btnFixFirewall.BackColor = System.Drawing.Color.FromArgb(60, 40, 20);
            btnFixFirewall.ForeColor = System.Drawing.Color.FromArgb(255, 180, 80);
            btnFixFirewall.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            btnFixFirewall.Click    += btnFixFirewall_Click;

            chkTailscale.Text     = "Tailscale Funnel";
            chkTailscale.Location = new System.Drawing.Point(392, 30);
            chkTailscale.Size     = new System.Drawing.Size(140, 20);
            chkTailscale.CheckedChanged += chkTailscale_CheckedChanged;

            lblUrlLabel.Text      = "Webhook URL:";
            lblUrlLabel.Location  = new System.Drawing.Point(550, 30);
            lblUrlLabel.Size      = new System.Drawing.Size(84, 20);
            lblUrlLabel.AutoSize  = false;
            lblUrlLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            lblWebhookUrl.Text      = "(server stopped)";
            lblWebhookUrl.Location  = new System.Drawing.Point(638, 30);
            lblWebhookUrl.Size      = new System.Drawing.Size(280, 20);
            lblWebhookUrl.AutoSize  = false;
            lblWebhookUrl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblWebhookUrl.Anchor    = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            btnCopyUrl.Text     = "Copy URL";
            btnCopyUrl.Location = new System.Drawing.Point(924, 26);
            btnCopyUrl.Size     = new System.Drawing.Size(68, 26);
            btnCopyUrl.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnCopyUrl.Click   += btnCopyUrl_Click;

            btnCopyPayload.Text     = "Copy Payload";
            btnCopyPayload.Location = new System.Drawing.Point(996, 26);
            btnCopyPayload.Size     = new System.Drawing.Size(68, 26);
            btnCopyPayload.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnCopyPayload.Click   += btnCopyPayload_Click;

            grpWebhook.Controls.AddRange(new System.Windows.Forms.Control[]
            {
                lblPortLabel, nudPort, btnStartStop, btnFixFirewall, chkTailscale,
                lblUrlLabel, lblWebhookUrl, btnCopyUrl, btnCopyPayload
            });

            // ── grpSecurity ────────────────────────────────────────────────
            grpSecurity.Text     = "Security";
            grpSecurity.Location = new System.Drawing.Point(12, 276);
            grpSecurity.Size     = new System.Drawing.Size(1074, 130);
            grpSecurity.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            lblApiKeyLabel.Text      = "API Key:";
            lblApiKeyLabel.Location  = new System.Drawing.Point(12, 28);
            lblApiKeyLabel.Size      = new System.Drawing.Size(58, 20);
            lblApiKeyLabel.AutoSize  = false;
            lblApiKeyLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            txtApiKey.Location       = new System.Drawing.Point(74, 26);
            txtApiKey.Size           = new System.Drawing.Size(300, 23);
            txtApiKey.ReadOnly       = true;
            txtApiKey.PlaceholderText = "(disabled — click Generate Key to enable)";

            btnGenerateKey.Text     = "Generate Key";
            btnGenerateKey.Location = new System.Drawing.Point(382, 24);
            btnGenerateKey.Size     = new System.Drawing.Size(100, 26);
            btnGenerateKey.Click   += btnGenerateKey_Click;

            btnClearKey.Text     = "Clear Key";
            btnClearKey.Location = new System.Drawing.Point(488, 24);
            btnClearKey.Size     = new System.Drawing.Size(80, 26);
            btnClearKey.Click   += btnClearKey_Click;

            lblIpModeLabel.Text      = "IP Filter:";
            lblIpModeLabel.Location  = new System.Drawing.Point(12, 64);
            lblIpModeLabel.Size      = new System.Drawing.Size(58, 20);
            lblIpModeLabel.AutoSize  = false;
            lblIpModeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            rbAnyIp.Text     = "Any IP";
            rbAnyIp.Location = new System.Drawing.Point(74, 64);
            rbAnyIp.Size     = new System.Drawing.Size(76, 20);
            rbAnyIp.Checked  = true;
            rbAnyIp.CheckedChanged += rbIpMode_CheckedChanged;

            rbTvOnly.Text     = "TradingView Only";
            rbTvOnly.Location = new System.Drawing.Point(156, 64);
            rbTvOnly.Size     = new System.Drawing.Size(140, 20);
            rbTvOnly.CheckedChanged += rbIpMode_CheckedChanged;

            rbCustomIp.Text     = "Custom:";
            rbCustomIp.Location = new System.Drawing.Point(302, 64);
            rbCustomIp.Size     = new System.Drawing.Size(68, 20);
            rbCustomIp.CheckedChanged += rbIpMode_CheckedChanged;

            txtCustomIps.Location      = new System.Drawing.Point(374, 62);
            txtCustomIps.Size          = new System.Drawing.Size(340, 23);
            txtCustomIps.PlaceholderText = "1.2.3.4, 5.6.7.8 (comma-separated)";
            txtCustomIps.Enabled       = false;
            txtCustomIps.TextChanged  += txtCustomIps_TextChanged;

            lblRateLimitLabel.Text      = "Max orders/min:";
            lblRateLimitLabel.Location  = new System.Drawing.Point(12, 100);
            lblRateLimitLabel.Size      = new System.Drawing.Size(105, 20);
            lblRateLimitLabel.AutoSize  = false;
            lblRateLimitLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            nudRateLimit.Location  = new System.Drawing.Point(120, 98);
            nudRateLimit.Size      = new System.Drawing.Size(65, 23);
            nudRateLimit.Minimum   = 1;
            nudRateLimit.Maximum   = 60;
            nudRateLimit.Value     = 10;
            nudRateLimit.ValueChanged += nudRateLimit_ValueChanged;

            chkLogToFile.Text     = "Log to file (TradeRouter.log)";
            chkLogToFile.Location = new System.Drawing.Point(220, 100);
            chkLogToFile.Size     = new System.Drawing.Size(220, 22);
            chkLogToFile.CheckedChanged += chkLogToFile_CheckedChanged;

            grpSecurity.Controls.AddRange(new System.Windows.Forms.Control[]
            {
                lblApiKeyLabel, txtApiKey, btnGenerateKey, btnClearKey,
                lblIpModeLabel, rbAnyIp, rbTvOnly, rbCustomIp, txtCustomIps,
                lblRateLimitLabel, nudRateLimit, chkLogToFile
            });

            // ── grpConsole (full-width, fills remaining height) ────────────
            grpConsole.Text     = "Console";
            grpConsole.Location = new System.Drawing.Point(12, 418);
            grpConsole.Size     = new System.Drawing.Size(1074, 326);
            grpConsole.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom
                                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            rtbConsole.ReadOnly     = true;
            rtbConsole.ScrollBars   = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            rtbConsole.WordWrap     = false;
            rtbConsole.Location     = new System.Drawing.Point(6, 22);
            rtbConsole.Size         = new System.Drawing.Size(1030, 270);
            rtbConsole.Anchor       = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom
                                    | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            rtbConsole.Font         = new System.Drawing.Font("Consolas", 9F);

            btnClearConsole.Text     = "Clear";
            btnClearConsole.Location = new System.Drawing.Point(1042, 22);
            btnClearConsole.Size     = new System.Drawing.Size(26, 22);
            btnClearConsole.Anchor   = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnClearConsole.Font     = new System.Drawing.Font("Segoe UI", 7F);
            btnClearConsole.Click   += (s, e) => rtbConsole.Clear();

            grpConsole.Controls.AddRange(new System.Windows.Forms.Control[]
            {
                rtbConsole, btnClearConsole
            });

            // ── Status strip ──────────────────────────────────────────────
            tsslStatus.Text      = "Ready.";
            tsslStatus.Spring    = true;
            tsslStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            statusStrip.Items.Add(tsslStatus);
            statusStrip.SizingGrip = false;

            // ── Add to form ───────────────────────────────────────────────
            Controls.AddRange(new System.Windows.Forms.Control[]
            {
                picLogo,
                grpNT8, grpWebhook, grpSecurity, grpConsole,
                statusStrip
            });

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
