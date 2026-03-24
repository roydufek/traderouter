using System;
using System.Collections.Generic;
using TradeRouter.Models;

namespace TradeRouter
{
    /// <summary>
    /// OrderMapper v1.0.0 — validates TradersPost payloads before forwarding to NT8 strategy.
    ///
    /// No action translation needed: the full JSON is forwarded as-is to WebhookOrderStrategy_v1_0_0
    /// which handles position state, risk caps, and execution natively inside NT8.
    ///
    /// This class is kept for UI position display and payload validation.
    /// </summary>
    public class OrderMapper
    {
        // Instrument map is retained for display and validation purposes.
        public static readonly Dictionary<string, string> InstrumentMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "YM1!",  "YM 06-26"  },
            { "MYM1!", "MYM 06-26" },
            { "NQ1!",  "NQ 06-26"  },
            { "MNQ1!", "MNQ 06-26" },
            { "ES1!",  "ES 06-26"  },
            { "MES1!", "MES 06-26" },
        };

        public enum PositionState { Flat, Long, Short }

        private PositionState _currentPosition = PositionState.Flat;
        public PositionState CurrentPosition => _currentPosition;

        public event EventHandler<PositionState>? PositionChanged;

        /// <summary>
        /// Validates the payload and returns a human-readable description of the order.
        /// Updates local position state for UI display.
        /// Returns null if the payload is invalid.
        /// </summary>
        public string? Validate(WebhookPayload payload, out string description)
        {
            description = string.Empty;

            string action    = payload.Action?.Trim().ToLowerInvariant()    ?? string.Empty;
            string sentiment = payload.Sentiment?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(sentiment))
                return null;

            if (!int.TryParse(payload.Quantity, out int quantity) || quantity <= 0)
                quantity = 1;

            string orderType = string.IsNullOrWhiteSpace(payload.OrderType) ? "MARKET" : payload.OrderType.ToUpperInvariant();

            // Validate limit orders have a price
            if (orderType == "LIMIT" && string.IsNullOrWhiteSpace(payload.LimitPrice))
            {
                description = "LIMIT order missing limitPrice field";
                return null;
            }

            // Determine display action for UI
            string displayAction = DetermineDisplayAction(action, sentiment);
            if (displayAction == null) return null;

            // Update local position state for UI
            UpdatePositionState(action, sentiment);

            description = $"{displayAction} {quantity}x {payload.Ticker} [{orderType}]";
            return description;
        }

        /// <summary>
        /// Legacy Map() shim — kept for compatibility with MainForm.
        /// v3: just validates and returns a dummy TradeOrder for logging; actual order is forwarded via NT8Client.
        /// </summary>
        public TradeOrder? Map(WebhookPayload payload, string account)
        {
            if (Validate(payload, out string desc) == null) return null;

            if (!int.TryParse(payload.Quantity, out int qty) || qty <= 0) qty = 1;

            return new TradeOrder
            {
                Account    = account,
                Action     = desc,           // human-readable description for logging
                Quantity   = qty,
                OrderType  = string.IsNullOrWhiteSpace(payload.OrderType) ? "MARKET" : payload.OrderType.ToUpperInvariant(),
                Instrument = MapInstrument(payload.Ticker),
                TIF        = "DAY"
            };
        }

        /// <summary>
        /// Sets the position state from external sync (e.g., if NT8 confirms state).
        /// </summary>
        public void SetPosition(PositionState state)
        {
            if (state != _currentPosition)
            {
                _currentPosition = state;
                PositionChanged?.Invoke(this, state);
            }
        }

        public void ResetPosition()
        {
            _currentPosition = PositionState.Flat;
            PositionChanged?.Invoke(this, PositionState.Flat);
        }

        private static string? DetermineDisplayAction(string action, string sentiment)
        {
            return (action, sentiment) switch
            {
                ("buy",  "long")  => "ENTER LONG",
                ("sell", "short") => "ENTER SHORT",
                ("sell", "flat")  => "EXIT LONG",
                ("buy",  "flat")  => "EXIT SHORT",
                _ => null
            };
        }

        private void UpdatePositionState(string action, string sentiment)
        {
            PositionState next = (action, sentiment) switch
            {
                ("buy",  "long")  => PositionState.Long,
                ("sell", "short") => PositionState.Short,
                ("sell", "flat")  => PositionState.Flat,
                ("buy",  "flat")  => PositionState.Flat,
                _ => _currentPosition
            };

            if (next != _currentPosition)
            {
                _currentPosition = next;
                PositionChanged?.Invoke(this, next);
            }
        }

        public static string MapInstrument(string ticker) =>
            InstrumentMap.TryGetValue(ticker, out string? mapped) ? mapped : ticker;

        public static string PositionStateToDisplayString(PositionState state) => state switch
        {
            PositionState.Flat  => "FLAT",
            PositionState.Long  => "LONG",
            PositionState.Short => "SHORT",
            _ => "UNKNOWN"
        };
    }
}
