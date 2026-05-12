namespace ForexAI.API.Models;

// ── Commands sent TO the MT5 EA (via /api/mt5/poll) ──────────────────────────

public class Mt5PollResponse
{
    public string CommandId { get; set; } = "";
    public string CommandType { get; set; } = "IDLE"; // IDLE | FETCH_DATA | EXECUTE_TRADE
    public string? Symbol { get; set; }
    public string? Timeframe { get; set; }
    public int CandleCount { get; set; } = 100;
    public string? TradeDirection { get; set; } // BUY | SELL
    public double LotSize { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
}

// ── Callbacks sent FROM the MT5 EA (via /api/mt5/callback) ───────────────────

public class Mt5CallbackRequest
{
    public string CommandId { get; set; } = "";
    public string CommandType { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    // FETCH_DATA payload
    public double AccountEquity { get; set; }
    public double AccountBalance { get; set; }
    public double AccountMarginUsed { get; set; }
    public double AccountMarginFree { get; set; }
    public int OpenPositionCount { get; set; }
    public List<Mt5CandleData>? Candles { get; set; }
    public string? Symbol { get; set; }
    public string? Timeframe { get; set; }

    // EXECUTE_TRADE payload
    public string? BrokerOrderId { get; set; }
    public double ExecutedPrice { get; set; }
}

public class Mt5CandleData
{
    public long Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
}
