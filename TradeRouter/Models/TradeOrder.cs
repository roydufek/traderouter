namespace TradeRouter.Models
{
    /// <summary>
    /// Represents a mapped trade order ready to send to NinjaTrader 8 ATI.
    /// </summary>
    public class TradeOrder
    {
        public string Account { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;   // BUY, SELL, SELLSHORT, BUYTOCOVER
        public int Quantity { get; set; } = 1;
        public string OrderType { get; set; } = "MARKET";
        public string Instrument { get; set; } = string.Empty;
        public string TIF { get; set; } = "DAY";

        /// <summary>
        /// Serializes the order to NT8 ATI XML format (newline-terminated).
        /// </summary>
        public string ToXml()
        {
            return $"<Order>\n" +
                   $"  <Account>{EscapeXml(Account)}</Account>\n" +
                   $"  <Action>{EscapeXml(Action)}</Action>\n" +
                   $"  <Quantity>{Quantity}</Quantity>\n" +
                   $"  <OrderType>{EscapeXml(OrderType)}</OrderType>\n" +
                   $"  <Instrument>{EscapeXml(Instrument)}</Instrument>\n" +
                   $"  <TIF>{EscapeXml(TIF)}</TIF>\n" +
                   $"</Order>\n";
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        public override string ToString()
        {
            return $"{Action} {Quantity}x {Instrument} [{OrderType}] on {Account}";
        }
    }
}
