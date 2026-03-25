// WebhookOrderStrategy_v1_0_8 — ships with TradeRouter v1.0.8
// Version this file together with TradeRouter releases. When strategy changes,
// bump the TradeRouter version and update the tag above.
#region Using declarations
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using System.Windows;
#endregion

// ────────────────────────────────────────────────────────────────────────────────
// WebhookOrderStrategy v3
// Aegis / Roy Dufek  2026-03-23
//
// Accepts the standard TradersPost JSON payload format from TradeRouter.
// Each strategy instance listens on its own configurable port (default 7091),
// so you can run multiple instances against different accounts like a trade copier.
//
// TradersPost JSON format (passthrough from TradingView):
//   {
//     "ticker":    "YM1!",
//     "action":    "buy" | "sell",
//     "sentiment": "long" | "short" | "flat",
//     "quantity":  "2",
//     "price":     "42100.0",          // entry/limit price reference
//     "orderType": "MARKET" | "LIMIT", // optional, default MARKET
//     "limitPrice": "42090.0",         // required when orderType=LIMIT
//     "time":      "2026-03-23T...",
//     "interval":  "30S"
//   }
//
// Action/sentiment → order mapping:
//   action=buy  + sentiment=long  → EnterLong  (market or limit)
//   action=sell + sentiment=short → EnterShort (market or limit)
//   action=sell + sentiment=flat  → ExitLong   (flatten long)
//   action=buy  + sentiment=flat  → ExitShort  (flatten short)
//
// Safety features:
//   • Daily max loss cap    — disables new entries for rest of day
//   • Daily profit target   — stops trading once target hit
//   • Per-trade max loss    — emergency market flatten on tick if unrealized exceeds limit
//   • Missed-flatten guard  — if new entry direction opposes open position and
//                             AllowReversals=false, auto-flattens first
//   • Startup flat check    — ignores all orders until account confirmed flat
//   • Per-day reset         — all caps reset at midnight; ordersDisabled clears automatically
//
// Multi-account / trade copier:
//   Run 3 instances of this strategy on 3 charts (same or different instruments),
//   each with a different ListenerPort (7091, 7092, 7093).
//   TradeRouter can fan-out the same webhook to all 3 ports simultaneously.
// ────────────────────────────────────────────────────────────────────────────────

namespace NinjaTrader.NinjaScript.Strategies
{
    public class WebhookOrderStrategy_v1_0_8 : Strategy
    {
        // ── Parameters ────────────────────────────────────────────────────────────

        [NinjaScriptProperty]
        [Range(1024, 65535)]
        [Display(Name = "Listener Port", Order = 1, GroupName = "Connection",
            Description = "Each strategy instance uses a unique port. Run multiple copies for trade copying across accounts.")]
        public int ListenerPort { get; set; } = 7091;

        [NinjaScriptProperty]
        [Range(1, 2)]
        [Display(Name = "Output Tab (1 or 2)", Order = 2, GroupName = "Connection",
            Description = "NT8 only has two output tabs. Instance 1 → Tab1, Instance 2 → Tab2.")]
        public int OutputTabNumber { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Daily Max Loss ($, 0=off)", Order = 1, GroupName = "Daily Caps",
            Description = "Stops new entries if today's realized P&L drops below this loss. Resets at midnight.")]
        public double DailyMaxLoss { get; set; } = 1000;

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Daily Profit Target ($, 0=off)", Order = 2, GroupName = "Daily Caps",
            Description = "Stops new entries once today's realized P&L reaches this profit. Resets at midnight.")]
        public double DailyProfitTarget { get; set; } = 2000;

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Per-Trade Max Loss ($, 0=off)", Order = 3, GroupName = "Daily Caps",
            Description = "Emergency market flatten if unrealized loss on open trade exceeds this. Backstop for missed exit webhooks.")]
        public double PerTradeMaxLoss { get; set; } = 500;

        [NinjaScriptProperty]
        [Display(Name = "Allow Reversals", Order = 1, GroupName = "Order Behavior",
            Description = "If true, a new entry in the opposite direction reverses the position in one order. If false, position is flattened first.")]
        public bool AllowReversals { get; set; } = false;

        [NinjaScriptProperty]
        [Range(100, 10000)]
        [Display(Name = "Position Confirm Timeout (ms)", Order = 2, GroupName = "Order Behavior",
            Description = "How long to wait for NT8 to confirm position change before logging a warning.")]
        public int ConfirmTimeoutMs { get; set; } = 3000;

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug Logging", Order = 3, GroupName = "Order Behavior")]
        public bool EnableDebug { get; set; } = true;

        // ── Internal state ────────────────────────────────────────────────────────

        private HttpListener     httpListener;
        private CancellationTokenSource cts;
        private Task             listenerTask;
        private Task             processorTask;
        private BlockingCollection<TpPayload> orderQueue;
        private ConcurrentQueue<string>       emergencyQueue = new ConcurrentQueue<string>();
        private TaskCompletionSource<bool>    orderSignal;

        // Daily P&L tracking
        private double   dailyStartPnl   = 0;
        private double   lastTradingDay_n = -1; // DayOfYear
        private bool     ordersDisabled   = false;
        private bool     waitingForFlat   = false;
        private bool     perTradeFlatPending = false;

        // ── Logging ───────────────────────────────────────────────────────────────

        private string _acctSuffix = "????";

        private string GetAcctSuffix()
        {
            try
            {
                string name = Account?.Name ?? "";
                return name.Length >= 4 ? name.Substring(name.Length - 4) : name.PadLeft(4, '?');
            }
            catch { return "????"; }
        }

        private PrintTo GetPrintTo() =>
            OutputTabNumber == 2 ? PrintTo.OutputTab2 : PrintTo.OutputTab1;

        private void Log(string msg)
        {
            if (EnableDebug)
            {
                PrintTo = GetPrintTo();
                Print($"[WOS1_0_8:{_acctSuffix}:{ListenerPort} {DateTime.Now:HH:mm:ss.fff}] {msg}");
            }
        }

        private void LogAlways(string msg)
        {
            PrintTo = GetPrintTo();
            Print($"[WOS1_0_8:{_acctSuffix}:{ListenerPort} {DateTime.Now:HH:mm:ss.fff}] {msg}");
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        protected override void OnStateChange()
        {
            try
            {
                switch (State)
                {
                    case State.SetDefaults:
                        Description     = $"WebhookOrderStrategy v3 — TradersPost JSON format, configurable port for multi-account trade copying.";
                        Name            = "WebhookOrderStrategy_v1_0_8";
                        Calculate       = Calculate.OnEachTick;
                        IsExitOnSessionCloseStrategy = false;
                        break;

                    case State.DataLoaded:
                        LogAlways($"Init — Port={ListenerPort} DailyMaxLoss={DailyMaxLoss} DailyPT={DailyProfitTarget} PerTradeLoss={PerTradeMaxLoss}");
                        InitState();
                        CheckStartupFlat();
                        StartProcessor();
                        StartListener();
                        break;

                    case State.Terminated:
                        LogAlways("Terminated.");
                        StopListener();
                        StopProcessor();
                        break;
                }
            }
            catch (Exception ex) { LogAlways($"OnStateChange error: {ex.Message}"); }
        }

        private void InitState()
        {
            cts          = new CancellationTokenSource();
            orderQueue   = new BlockingCollection<TpPayload>(100);
            emergencyQueue = new ConcurrentQueue<string>();
            orderSignal  = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ordersDisabled    = false;
            waitingForFlat    = false;
            perTradeFlatPending = false;

            _acctSuffix = GetAcctSuffix();
            ResetDailyPnl();
        }

        private void ResetDailyPnl()
        {
            dailyStartPnl    = GetCumRealizedPnl();
            lastTradingDay_n = DateTime.Today.DayOfYear;
            LogAlways($"Daily P&L window reset. Baseline: {dailyStartPnl:C2}");
        }

        // ── OnBarUpdate (tick-level checks) ───────────────────────────────────────

        protected override void OnBarUpdate()
        {
            // New day → reset caps
            if (DateTime.Today.DayOfYear != lastTradingDay_n)
            {
                ordersDisabled = false;
                ResetDailyPnl();
                LogAlways("New trading day — caps reset, trading re-enabled.");
            }

            if (ordersDisabled || waitingForFlat) return;

            // Per-trade max loss guard (checked every tick)
            CheckPerTradeLoss();

            // Also clear waitingForFlat here in case OnPositionUpdate didn't fire
            if (waitingForFlat && GetCurrentMarketPosition() == MarketPosition.Flat)
            {
                waitingForFlat = false;
                LogAlways("Account confirmed flat — orders enabled.");
            }
        }

        private void CheckPerTradeLoss()
        {
            if (PerTradeMaxLoss <= 0)          return;
            if (Position == null)              return;
            if (Position.MarketPosition == MarketPosition.Flat) return;
            if (perTradeFlatPending)           return;

            double px = Position.MarketPosition == MarketPosition.Long
                ? GetCurrentBid() : GetCurrentAsk();
            double unrealizedLoss = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, px);

            if (unrealizedLoss <= -PerTradeMaxLoss)
            {
                LogAlways($"⚠ Per-trade max loss hit: unrealized={unrealizedLoss:C2} limit={-PerTradeMaxLoss:C2}. Emergency flatten.");
                perTradeFlatPending = true;
                emergencyQueue.Enqueue("flatten");
                SignalProcessor();
            }
        }

        // ── P&L helpers ───────────────────────────────────────────────────────────

        private double GetCumRealizedPnl()
        {
            try { return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit; }
            catch { return 0; }
        }

        private double GetDailyPnl() => GetCumRealizedPnl() - dailyStartPnl;

        /// Returns true and logs if either daily cap is breached.
        private bool IsDailyCapBreached()
        {
            double pnl = GetDailyPnl();
            if (DailyMaxLoss > 0 && pnl <= -DailyMaxLoss)
            {
                LogAlways($"🛑 Daily max loss reached: {pnl:C2} (limit -${DailyMaxLoss}). No new entries today.");
                return true;
            }
            if (DailyProfitTarget > 0 && pnl >= DailyProfitTarget)
            {
                LogAlways($"✅ Daily profit target reached: {pnl:C2} (target ${DailyProfitTarget}). No new entries today.");
                return true;
            }
            return false;
        }

        // ── HTTP Listener ─────────────────────────────────────────────────────────

        private void StartListener()
        {
            try
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add($"http://+:{ListenerPort}/");
                httpListener.Start();
                listenerTask = Task.Run(() => ListenLoop(cts.Token), cts.Token);
                LogAlways($"✓ Listening on http://+:{ListenerPort}/");
            }
            catch (Exception ex)
            {
                LogAlways($"✗ Failed to start listener on port {ListenerPort}: {ex.Message}");
                LogAlways($"  Fix: run as admin → netsh http add urlacl url=http://+:{ListenerPort}/ user=Everyone");
            }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch (HttpListenerException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        Log($"ListenLoop error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            string body = "";
            try
            {
                var req = ctx.Request;
                if (!req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                { SendResponse(ctx, 405, "Method Not Allowed"); return; }

                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = reader.ReadToEnd();

                Log($"← {req.RemoteEndPoint?.Address}: {body}");

                var payload = TpPayload.Parse(body);
                if (payload == null)
                { SendResponse(ctx, 400, "Bad Request — could not parse TradersPost JSON"); return; }

                if (!orderQueue.TryAdd(payload, 100))
                { SendResponse(ctx, 429, "Too Many Requests"); return; }

                SignalProcessor();
                SendResponse(ctx, 200, $"OK action={payload.Action} sentiment={payload.Sentiment} qty={payload.Quantity}");
            }
            catch (Exception ex)
            {
                Log($"HandleRequest error: {ex.Message} body={body}");
                try { SendResponse(ctx, 500, "Internal Error"); } catch { }
            }
        }

        private static void SendResponse(HttpListenerContext ctx, int code, string text)
        {
            try
            {
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                byte[] buf = Encoding.UTF8.GetBytes(text);
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            }
            finally { try { ctx.Response.OutputStream.Close(); } catch { } }
        }

        private void StopListener()
        {
            try { cts?.Cancel(); httpListener?.Stop(); listenerTask?.Wait(500); httpListener?.Close(); }
            catch (Exception ex) { Log($"StopListener: {ex.Message}"); }
        }

        // ── Order Processor ───────────────────────────────────────────────────────

        private void StartProcessor()
        {
            processorTask = Task.Run(ProcessLoop);
            Log("Processor started.");
        }

        private void StopProcessor()
        {
            try { orderQueue?.CompleteAdding(); processorTask?.Wait(1000); orderQueue?.Dispose(); }
            catch (Exception ex) { Log($"StopProcessor: {ex.Message}"); }
        }

        private void SignalProcessor()
        {
            var sig = orderSignal;
            if (sig != null && !sig.Task.IsCompleted)
                sig.TrySetResult(true);
        }

        private async Task ProcessLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await orderSignal.Task;
                    orderSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    // Emergency flatten first
                    while (emergencyQueue.TryDequeue(out _))
                        await Dispatch(() => ExecuteFlatten("emergency"));

                    // Normal queue
                    while (orderQueue.TryTake(out TpPayload payload, 50, cts.Token))
                        await Dispatch(() => ExecutePayload(payload));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log($"ProcessLoop error: {ex.Message}"); await Task.Delay(100); }
            }
        }

        private async Task Dispatch(Action action)
        {
            if (Application.Current?.Dispatcher != null)
                await Application.Current.Dispatcher.InvokeAsync(action);
            else
                action();
        }

        // ── Command Execution ─────────────────────────────────────────────────────

        private void ExecuteFlatten(string reason)
        {
            var mp = GetCurrentMarketPosition();
            if (mp == MarketPosition.Flat) { Log($"Flatten ({reason}): already flat."); perTradeFlatPending = false; return; }

            Log($"Flattening ({reason}), pos={mp}");
            try
            {
                if (mp == MarketPosition.Long) ExitLong();
                else                           ExitShort();
            }
            catch (Exception ex) { LogAlways($"Flatten error ({reason}): {ex.Message}"); }
            perTradeFlatPending = false;
            LogPnlSnapshot($"flatten:{reason}");

            // After any flatten, re-check daily cap
            if (IsDailyCapBreached()) ordersDisabled = true;
        }

        private void ExecuteAccountFlatten(string reason)
        {
            LogAlways($"⚠ Account flatten ({reason}) — closing all positions on account.");
            try
            {
                Account.Flatten(new Instrument[] { Instrument });
            }
            catch (Exception ex) { LogAlways($"Account flatten error ({reason}): {ex.Message}"); }
            perTradeFlatPending = false;
            LogPnlSnapshot($"account-flatten:{reason}");
            if (IsDailyCapBreached()) ordersDisabled = true;
        }

        private void ExecutePayload(TpPayload p)
        {
            Log($"ExecutePayload: action={p.Action} sentiment={p.Sentiment} qty={p.Quantity} orderType={p.OrderType} limitPrice={p.LimitPrice}");

            // Startup flat guard
            if (waitingForFlat)
            { Log("Ignoring — waiting for account flat at startup."); return; }

            // ── Exits — processed even when ordersDisabled ─────────────────────
            bool isExit = p.Sentiment == "flat";

            // action=flatten → nuclear account flatten (from Emergency Flatten button)
            if (p.Action == "flatten")
            {
                ExecuteAccountFlatten("emergency-webhook");
                return;
            }

            if (isExit)
            {
                var mp = GetCurrentMarketPosition();
                if (p.Action == "sell" && mp == MarketPosition.Long)
                {
                    Log("ExitLong (exit-long)");
                    try { ExitLong(); } catch (Exception ex) { LogAlways($"ExitLong error: {ex.Message}"); }
                    LogPnlSnapshot("exit-long");
                    if (IsDailyCapBreached()) ordersDisabled = true;
                }
                else if (p.Action == "buy" && mp == MarketPosition.Short)
                {
                    Log("ExitShort (exit-short)");
                    try { ExitShort(); } catch (Exception ex) { LogAlways($"ExitShort error: {ex.Message}"); }
                    LogPnlSnapshot("exit-short");
                    if (IsDailyCapBreached()) ordersDisabled = true;
                }
                else
                {
                    Log($"Exit ignored — action={p.Action} but position={mp}");
                }
                return;
            }

            // ── Entries — blocked by daily cap ────────────────────────────────
            if (ordersDisabled)
            { LogAlways($"Orders disabled (daily cap). Ignoring {p.Action}/{p.Sentiment}."); return; }

            if (IsDailyCapBreached())
            {
                ordersDisabled = true;
                // If somehow in a position, flatten it
                if (GetCurrentMarketPosition() != MarketPosition.Flat) ExecuteFlatten("daily-cap-on-entry");
                return;
            }

            var cur = GetCurrentMarketPosition();

            // ── Long entry ────────────────────────────────────────────────────
            if (p.Action == "buy" && p.Sentiment == "long")
            {
                if (cur == MarketPosition.Long) { Log("Already long — ignoring."); return; }

                if (cur == MarketPosition.Short)
                {
                    if (AllowReversals)
                    {
                        Log($"Reversing short→long qty={p.Quantity} type={p.OrderType}");
                        PlaceEntry(MarketDirection.Long, p, "RevToLong");
                    }
                    else
                    {
                        LogAlways("⚠ AllowReversals=false: flattening short, not entering long.");
                        ExecuteFlatten("missed-flatten");
                    }
                    return;
                }

                Log($"EnterLong qty={p.Quantity} type={p.OrderType}");
                PlaceEntry(MarketDirection.Long, p, "FlatBuy");
                return;
            }

            // ── Short entry ───────────────────────────────────────────────────
            if (p.Action == "sell" && p.Sentiment == "short")
            {
                if (cur == MarketPosition.Short) { Log("Already short — ignoring."); return; }

                if (cur == MarketPosition.Long)
                {
                    if (AllowReversals)
                    {
                        Log($"Reversing long→short qty={p.Quantity} type={p.OrderType}");
                        PlaceEntry(MarketDirection.Short, p, "RevToShort");
                    }
                    else
                    {
                        LogAlways("⚠ AllowReversals=false: flattening long, not entering short.");
                        ExecuteFlatten("missed-flatten");
                    }
                    return;
                }

                Log($"EnterShort qty={p.Quantity} type={p.OrderType}");
                PlaceEntry(MarketDirection.Short, p, "FlatSell");
                return;
            }

            LogAlways($"Unrecognised action/sentiment combination: action={p.Action} sentiment={p.Sentiment}");
        }

        /// Places either a market or limit entry depending on orderType in the payload.
        private void PlaceEntry(MarketDirection direction, TpPayload p, string signalName)
        {
            try
            {
                bool isLong = direction == MarketDirection.Long;

                if (p.OrderType == "LIMIT" && p.LimitPrice > 0)
                {
                    // Limit order
                    if (isLong)
                        EnterLongLimit(p.Quantity, p.LimitPrice, signalName);
                    else
                        EnterShortLimit(p.Quantity, p.LimitPrice, signalName);

                    Log($"Placed LIMIT {(isLong ? "LONG" : "SHORT")} qty={p.Quantity} @ {p.LimitPrice}");
                }
                else
                {
                    // Market order (default)
                    if (isLong)
                        EnterLong(p.Quantity, signalName);
                    else
                        EnterShort(p.Quantity, signalName);

                    Log($"Placed MARKET {(isLong ? "LONG" : "SHORT")} qty={p.Quantity}");
                }
            }
            catch (Exception ex)
            {
                LogAlways($"PlaceEntry error ({signalName}): {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private MarketPosition GetCurrentMarketPosition() =>
            Position?.MarketPosition ?? MarketPosition.Flat;

        private void LogPnlSnapshot(string ctx)
        {
            try
            {
                double daily = GetDailyPnl();
                double cum   = GetCumRealizedPnl();
                double bal   = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                LogAlways($"P&L [{ctx}] Today: {daily:C2}  Cumulative: {cum:C2}  Balance: {bal:C2}");
            }
            catch { }
        }

        private void CheckStartupFlat()
        {
            try
            {
                foreach (Position pos in Account.Positions)
                {
                    if (pos.Instrument.FullName == Instrument.FullName && pos.Quantity > 0)
                    {
                        LogAlways($"⚠ Startup: open position {pos.MarketPosition} x{pos.Quantity} @ {pos.AveragePrice}. Waiting for flat.");
                        waitingForFlat = true;
                        return;
                    }
                }
                LogAlways("Startup: account flat. Ready.");
                waitingForFlat = false;
            }
            catch (Exception ex) { LogAlways($"CheckStartupFlat error: {ex.Message}"); waitingForFlat = false; }
        }

        private enum MarketDirection { Long, Short }
    }

    // ── TradersPost JSON payload ──────────────────────────────────────────────────
    // Matches the payload format used by TradingView Pine strategy alert_message.
    //
    // Supported fields:
    //   ticker     (string)  — instrument ticker, e.g. "YM1!"
    //   action     (string)  — "buy" | "sell"
    //   sentiment  (string)  — "long" | "short" | "flat"
    //   quantity   (string|int) — number of contracts
    //   price      (string|float) — bar close / signal price (reference only)
    //   orderType  (string)  — "MARKET" (default) | "LIMIT"
    //   limitPrice (string|float) — limit price (required when orderType=LIMIT)
    //   time       (string)  — ISO timestamp from TV
    //   interval   (string)  — chart interval from TV

    public class TpPayload
    {
        public string Ticker      { get; set; } = "";
        public string Action      { get; set; } = "";   // "buy" | "sell"
        public string Sentiment   { get; set; } = "";   // "long" | "short" | "flat"
        public int    Quantity    { get; set; } = 1;
        public double Price       { get; set; } = 0;    // signal/close price reference
        public string OrderType   { get; set; } = "MARKET";
        public double LimitPrice  { get; set; } = 0;
        public string Time        { get; set; } = "";
        public string Interval    { get; set; } = "";

        /// Lightweight JSON parser — no Newtonsoft dependency required.
        public static TpPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var p = new TpPayload();
                p.Ticker     = GetStr(json, "ticker")     ?? "";
                p.Action     = (GetStr(json, "action")    ?? "").ToLowerInvariant().Trim();
                p.Sentiment  = (GetStr(json, "sentiment") ?? "").ToLowerInvariant().Trim();
                p.Time       = GetStr(json, "time")       ?? "";
                p.Interval   = GetStr(json, "interval")   ?? "";
                p.OrderType  = (GetStr(json, "orderType") ?? "MARKET").ToUpperInvariant().Trim();

                string qty   = GetStr(json, "quantity");
                if (qty != null && int.TryParse(qty, out int q) && q > 0) p.Quantity = q;

                string price = GetStr(json, "price");
                if (price != null && double.TryParse(price, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double pr)) p.Price = pr;

                string lp = GetStr(json, "limitPrice");
                if (lp != null && double.TryParse(lp, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double lpr)) p.LimitPrice = lpr;

                // Validate required fields
                if (string.IsNullOrEmpty(p.Action) || string.IsNullOrEmpty(p.Sentiment))
                    return null;

                return p;
            }
            catch { return null; }
        }

        private static string GetStr(string json, string key)
        {
            string k = $"\"{key}\"";
            int ki = json.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (ki < 0) return null;
            int colon = json.IndexOf(':', ki + k.Length);
            if (colon < 0) return null;
            int s = colon + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            if (s >= json.Length) return null;
            if (json[s] == '"')
            {
                int e = json.IndexOf('"', s + 1);
                return e < 0 ? null : json.Substring(s + 1, e - s - 1);
            }
            else
            {
                int e = s;
                while (e < json.Length && json[e] != ',' && json[e] != '}' && json[e] != '\n') e++;
                return json.Substring(s, e - s).Trim().Trim('"');
            }
        }
    }
}
