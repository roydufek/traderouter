<p align="center">
  <img src="assets/logo.png" alt="TradeRouter" width="96" height="96" />
</p>

<h1 align="center">TradeRouter</h1>

<p align="center">
  <strong>TradingView → NinjaTrader 8 webhook bridge. Local, zero-latency. Multi-account trade copying via configurable ports.</strong>
</p>

---

## What it does

TradingView fires a webhook alert. TradeRouter receives it locally on your Windows machine and forwards it to one or more `WebhookOrderStrategy_v1` instances running inside NinjaTrader 8 — each managing its own account, P&L caps, and risk controls independently.

```
TradingView Alert (HTTPS)
        ↓
TradeRouter.exe  (Windows, Tailscale Funnel)
  ├── POST http://127.0.0.1:7091/  → WebhookOrderStrategy_v1 (Apex account)
  ├── POST http://127.0.0.1:7092/  → WebhookOrderStrategy_v1 (Lucid account)  [optional]
  └── POST http://127.0.0.1:7093/  → WebhookOrderStrategy_v1 (Sim account)    [optional]
```

**Why not TradersPost?** TradersPost adds 500ms–2s of webhook relay latency. On fast YM moves that's 15–20 points of slippage per trade. TradeRouter is local — sub-10ms from alert to order.

---

## Quick Start

### 1. Download

Grab `TradeRouter.exe` and `WebhookOrderStrategy_v1.cs` from [Releases](https://github.com/roydufek/traderouter/releases/latest). Place the exe in its own folder — `TradeRouter.ini` is auto-created there on first run.

### 2. Install Tailscale (optional — for TradingView webhooks)

[Download Tailscale](https://tailscale.com/download). Enable **Funnel** for your machine to get a public HTTPS URL that TradingView can reach.

TradeRouter starts and stops the Tailscale Funnel automatically via the **Tailscale Funnel** checkbox. If Funnel isn't enabled on your tailnet yet, TradeRouter will print the direct enable URL when you try to start.

> **Local testing only?** Skip Tailscale entirely and point your webhook at `http://localhost:7890/webhook/`.

### 3. Load the NT8 Strategy

Copy `WebhookOrderStrategy_v1.cs` into your NinjaTrader 8 NinjaScript folder:

```
Documents\NinjaTrader 8\bin\Custom\Strategies\
```

Then in NT8: **NinjaScript Editor → F5 to compile**. Once it compiles cleanly, you're ready.

### 4. Register Ports (one-time, admin required)

NT8's `HttpListener` requires a URL reservation per port. In TradeRouter:

1. Set **Strategy Port** to `7091`
2. Click **Register Ports** → accept the UAC prompt

TradeRouter handles both the `netsh` urlacl reservation and the Windows Firewall rule automatically.

Or run manually as admin:
```batch
netsh http add urlacl url=http://+:7091/ user=Everyone
netsh advfirewall firewall add rule name="TradeRouter Port 7091" dir=in action=allow protocol=TCP localport=7091
```

### 5. Start the NT8 Strategy

In NinjaTrader 8: **New → Strategies → Add → WebhookOrderStrategy_v1**

Configure parameters:

| Parameter | Description |
|---|---|
| Listener Port | `7091` — must match TradeRouter's Strategy Port |
| Output Tab | `1` or `2` — NT8 has exactly two output tabs |
| Daily Max Loss | e.g. `1000` — stops new entries if today's P&L drops $1,000 |
| Daily Profit Target | e.g. `2000` — stops new entries once $2,000 is banked today |
| Per-Trade Max Loss | e.g. `500` — emergency flatten if unrealized loss exceeds $500 |
| Allow Reversals | `false` (safer) — opposing entry flattens current position first |

Enable the strategy. In NT8 **Output Window** (Ctrl+Shift+O → Tab 1):
```
[WOS1:XXXX:7091 HH:mm:ss] ✓ Listening on http://+:7091/
```

### 6. Configure TradeRouter

1. Set **Strategy Port** = `7091`
2. Optionally add **Copy Ports** = `7092, 7093` for additional accounts
3. Set **Listen Port** = `7890` (inbound webhooks from TradingView)
4. Enable **Tailscale Funnel** if using TradingView
5. Click **Start Server** → then **Connect**

### 7. Set Up TradingView Alert

In your Pine strategy, set the alert message to:

```json
{
  "action": "{{strategy.order.action}}",
  "sentiment": "{{strategy.market_position}}",
  "quantity": "{{strategy.order.contracts}}",
  "price": "{{close}}",
  "time": "{{timenow}}"
}
```

If API key auth is enabled, add it as the first field:
```json
{
  "api": "YOUR_API_KEY_HERE",
  "action": "{{strategy.order.action}}",
  "sentiment": "{{strategy.market_position}}",
  "quantity": "{{strategy.order.contracts}}",
  "price": "{{close}}",
  "time": "{{timenow}}"
}
```

Use **Copy Payload** in TradeRouter to get the pre-filled JSON with your current settings.

Set the **Webhook URL** to your Tailscale Funnel URL shown in TradeRouter after the server starts.

> **Alert trigger setting:** Use **Once Per Bar Close** or **Once Per Bar** — not "Once Only", which prevents re-fires.

---

## Multi-Account Trade Copying

Run multiple instances of `WebhookOrderStrategy_v1` in NT8 — one per account, each on its own port:

| Instance | Port | Account | Output Tab |
|---|---|---|---|
| 1 | 7091 | Apex eval | 1 |
| 2 | 7092 | Lucid eval | 2 |
| 3 | 7093 | Sim | 2 |

In TradeRouter: **Copy Ports** = `7092, 7093`

Every TradingView alert fans out to all ports simultaneously. Each strategy manages its own account's P&L caps — if the Apex instance hits its daily limit and stops, Lucid and Sim keep running.

---

## Payload Reference

| Field | TV Variable | Description |
|---|---|---|
| `action` | `{{strategy.order.action}}` | `"buy"` or `"sell"` |
| `sentiment` | `{{strategy.market_position}}` | `"long"`, `"short"`, or `"flat"` |
| `quantity` | `{{strategy.order.contracts}}` | Number of contracts |
| `price` | `{{close}}` | Signal bar close price (reference) |
| `time` | `{{timenow}}` | ISO timestamp |
| `api` | _(static)_ | API key (optional, only if auth enabled) |

### Order Routing Logic

| action | sentiment | Result |
|---|---|---|
| `buy` | `long` | Enter long |
| `sell` | `short` | Enter short |
| `sell` | `flat` | Exit long (flatten) |
| `buy` | `flat` | Exit short (flatten) |
| `flatten` | _(any)_ | Emergency flatten regardless of position |

---

## Emergency Flatten

The **⚠ EMERGENCY FLATTEN ALL** button sends a `flatten` command to all configured ports simultaneously. The NT8 strategy calls `ExitLong()` + `ExitShort()` regardless of current position. Use this if webhooks stop firing and you need to get flat immediately.

Manual flatten via curl:
```bash
curl -X POST http://127.0.0.1:7091/ -H "Content-Type: application/json" \
  -d '{"action":"flatten"}'
```

---

## Security

**API Key** — Optional. Generate in TradeRouter and include in TradingView alerts as `"api": "KEY"`. Requests with wrong or missing keys are silently dropped (no response body, no timing leak).

**IP Allowlist** — Three modes:
- **Any IP** — no restriction (use with API key)
- **TradingView Only** — whitelists TradingView's 4 known IPs
- **Custom** — comma-separated list

**Rate Limiting** — Max orders per minute (default 10). Excess requests return HTTP 429.

---

## NT8 Strategy Reference

### Parameters

| Parameter | Default | Description |
|---|---|---|
| Listener Port | `7091` | HTTP port this instance listens on. Must be unique per instance. |
| API Key | _(blank)_ | Optional auth key. |
| Output Tab | `1` | NT8 has exactly two output tabs (`1` or `2`). No other values are valid. |
| Daily Max Loss | `$1,000` | No new entries if today's realized P&L drops this far. `0` = off. |
| Daily Profit Target | `$2,000` | Stops new entries once today's target is reached. `0` = off. |
| Per-Trade Max Loss | `$500` | Emergency market flatten if unrealized loss exceeds this on any tick. `0` = off. |
| Allow Reversals | `false` | If false, opposing entry flattens current position first. |
| Enable Debug | `true` | Verbose logging to output tab. |

### Daily P&L Caps
- Measured as realized P&L delta from strategy load (resets at midnight)
- When breached: no new entries for the rest of the day
- Exit/flatten signals always execute even when caps are hit
- Resets automatically at midnight

### Per-Trade Backstop
Checked on every tick while in a position. If unrealized loss exceeds `PerTradeMaxLoss`, an emergency market flatten fires immediately — protecting against scenarios where the exit webhook never arrives.

### Log Prefix
```
[WOS1:{account-last-4}:{port} HH:mm:ss.fff]
```
Example: `[WOS1:0007:7091 14:23:01.442]` — makes it easy to distinguish multiple instances in the same output tab.

### netsh Requirement
NT8's `HttpListener` requires a URL reservation per port. Use **Register Ports** in TradeRouter, or run manually as admin:
```batch
netsh http add urlacl url=http://+:7091/ user=Everyone
```

---

## Console Output

| Prefix | Meaning |
|---|---|
| `→ Webhook:` | Incoming TV alert (raw JSON) |
| `► Forwarding:` | Payload sent to NT8 |
| `◀ NT8:7091` | Response from NT8 strategy |
| `⚠` | Warning (dropped order, disconnect) |
| `✗` | Error |

---

## Startup Self-Test

On launch, TradeRouter automatically checks:
- NT8 strategy port reachable
- Webhook port urlacl registered
- Webhook port Windows Firewall rule present
- Tailscale available in PATH
- Current Tailscale Funnel status

Warnings appear in the console with the exact button to click to fix them.

---

## Building from Source

```bash
git clone https://github.com/roydufek/traderouter.git
cd traderouter
dotnet build TradeRouter/TradeRouter.csproj -c Release
```

Requires .NET 8 SDK. GitHub Actions builds automatically on every push to `main` and publishes a release on `v*` tags.

---

## Files

| File | Description |
|---|---|
| `TradeRouter.exe` | Main application (portable, no installer needed) |
| `TradeRouter.ini` | Settings file (auto-created next to exe on first run) |
| `TradeRouter.log` | Log file |
| `NinjaTrader/WebhookOrderStrategy_v1.cs` | NT8 NinjaScript strategy |

---

## License

MIT — see [LICENSE](LICENSE)
