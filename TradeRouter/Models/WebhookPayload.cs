using System;
using System.Text.Json.Serialization;

namespace TradeRouter.Models
{
    /// <summary>
    /// Represents the TradersPost-format JSON webhook payload received from TradingView.
    /// v1.0.0: added orderType and limitPrice for limit order support; passthrough to NT8 strategy.
    /// </summary>
    public class WebhookPayload
    {
        [JsonPropertyName("ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("sentiment")]
        public string Sentiment { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public string Quantity { get; set; } = "1";

        [JsonPropertyName("price")]
        public string Price { get; set; } = string.Empty;

        /// <summary>"MARKET" (default) or "LIMIT"</summary>
        [JsonPropertyName("orderType")]
        public string OrderType { get; set; } = "MARKET";

        /// <summary>Required when OrderType == "LIMIT".</summary>
        [JsonPropertyName("limitPrice")]
        public string LimitPrice { get; set; } = string.Empty;

        [JsonPropertyName("time")]
        public string Time { get; set; } = string.Empty;

        [JsonPropertyName("interval")]
        public string Interval { get; set; } = string.Empty;

        /// <summary>Optional API key embedded in payload (alternative to X-Api-Key header).</summary>
        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }

        public override string ToString() =>
            $"ticker={Ticker} action={Action} sentiment={Sentiment} qty={Quantity} type={OrderType}";

        /// <summary>
        /// Serializes this payload back to JSON for forwarding to the NT8 strategy HTTP listener.
        /// Strips the internal apiKey field before forwarding.
        /// </summary>
        public string ToForwardJson()
        {
            // Build clean JSON matching TpPayload.Parse() in WebhookOrderStrategy_v1_0_0.cs
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                ticker     = Ticker,
                action     = Action,
                sentiment  = Sentiment,
                quantity   = Quantity,
                price      = Price,
                orderType  = string.IsNullOrWhiteSpace(OrderType) ? "MARKET" : OrderType,
                limitPrice = LimitPrice,
                time       = Time,
                interval   = Interval
            });
        }
    }
}
