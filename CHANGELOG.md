# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0] - 2026-03-24

### Added

- **NT8 ATI Connection State Machine** — proper Disconnected / Connecting / Connected / Failed states with color-coded indicator (gray / amber / green / red dot). Connected state is only shown after accounts are verified.
- **Position Sync from NT8** — auto-queries market position on connect and on a configurable timer (default 30 s, min 5 s, max 300 s). Adds "Auto-sync position" checkbox, interval NumericUpDown, and "Sync Now" button.
- **Position display** shows quantity for LONG N / SHORT N (e.g., "LONG 2").
- **Position reconciliation log** — if NT8 reports a different position than local state, logs `"Position reconciled: SHORT → FLAT (NT8 sync)"`.
- **Position note label** — shows "⚠ Verify with broker" on startup until first NT8 sync completes; updates to sync timestamp after.
- **Tailscale pre-flight checks** — checks `tailscale status` before starting funnel. Detects: not running, certificate errors, port-in-use conflicts. Shows descriptive error in UI and log. Disables checkbox if tailscale binary not in PATH.
- **API Key Authentication** — UUID v4 key generation, "Generate Key" and "Clear Key" buttons, read-only TextBox display. When a key is active all incoming requests must include `"apiKey"` in JSON payload. Wrong/missing key: silent drop (no HTTP response).
- **Copy Payload button** — copies TradingView-ready JSON template to clipboard, including `apiKey` field when one is active. Omits `apiKey` when auth is disabled.
- **IP Allowlist** — radio group: Any IP / TradingView Only (hardcoded IPs) / Custom (comma-separated TextBox). Blocked requests are silently dropped and logged.
- **Rate Limiter** — sliding window per source IP, configurable 1–60 orders/min (default 10). Exceeded requests silently dropped and logged.
- **INI Persistence** (`TradeRouter.ini` in exe directory) — all settings load on startup and save on any change. Auto-creates with defaults on first run.
- **Log to File** — checkbox persisted in INI. Appends all webhook/order/error events to `TradeRouter.log` with `[INFO]`/`[WARN]`/`[ERROR]` levels. Rotates at 10 MB (renames to `.log.bak`).
- **Startup Self-Test** — on launch checks NT8 port 36973 reachability (TCP, no commands) and tailscale binary availability. Logs results and displays in status bar for 5 seconds.
- **Security GroupBox** — new panel in UI containing API key controls, IP allowlist radio buttons, custom IP TextBox, rate limit NumericUpDown, and Log-to-file checkbox.
- **`SecurityManager` class** — thread-safe IP allowlist, API key validation, and sliding-window rate limiter.
- **`IniFile` class** — simple thread-safe INI reader/writer with section support.
- **`FileLogger` class** — thread-safe file logger with 10 MB rotation.
- **`PositionQueryResult` class** — result type for NT8 market position queries.

### Fixed

- **Connected state bug** — app no longer shows "Connected" if account query fails, times out, or returns no accounts. Now transitions to `Failed: <reason>` with the specific error message shown in the status bar and connection label.
- **Tailscale cert errors swallowed silently** — stderr is now captured and inspected; certificate errors, port conflicts, and daemon-not-running states all produce user-visible error messages.
- **Disconnected dot was red** — Disconnected state now uses gray dot (red is reserved for Failed).

### Changed

- `NT8Client.ConnectionState` enum gains `Failed` state; `Disconnected` dot color changed from red to gray.
- `TailscaleFunnel.ErrorOccurred` event changed from `EventHandler<Exception>` to `EventHandler<string>` for cleaner error message propagation.
- `OrderMapper` gains `SetPosition(PositionState)` method for external (NT8 sync) position updates.
- `WebhookPayload` gains optional `apiKey` field.
- `WebhookServer` gains `Security` property (injected `SecurityManager`) that gates each request through IP, key, and rate-limit checks before processing.
- Form size increased to 1200×820 to accommodate Security panel.
- Status bar messages now use `tsslStatus` (consistent label reference).
- `TradeRouter.csproj` version bumped to `2.0.0`.

## [1.0.0] - 2026-03-23

### Added

- Initial release
- TradingView webhook receiver (HTTP POST /webhook)
- NinjaTrader 8 ATI TCP connection with account query
- Order mapping: BUY / SELL / SELLSHORT / BUYTOCOVER
- Instrument mapping table (YM, MYM, NQ, MNQ, ES, MES)
- Position state tracking (FLAT / LONG / SHORT)
- Tailscale Funnel integration for public HTTPS exposure
- Dark-themed WinForms UI with two log panels
- GitHub Actions CI/CD: self-contained win-x64 single-file exe
- MIT License
