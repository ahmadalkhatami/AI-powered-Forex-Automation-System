using ForexAI.Domain.Enums;

namespace ForexAI.Domain.Entities;

public class TradePosition
{
    public string TradeId { get; private set; }
    public string RunId { get; private set; }
    public TradeStatus Status { get; private set; }
    public string Pair { get; private set; }
    public SignalDirection Direction { get; private set; }
    public decimal Entry { get; private set; }
    public decimal StopLoss { get; private set; }
    public decimal TakeProfit { get; private set; }
    public decimal LotSize { get; private set; }
    public decimal RiskAmount { get; private set; }
    public decimal PotentialProfit { get; private set; }
    public decimal RiskReward { get; private set; }
    public decimal FloatingPnl { get; private set; }
    public int FloatingPnlPips { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string Mode { get; private set; }
    public string? SkipReason { get; private set; }
    public string? ExternalTradeId { get; private set; }
    /// <summary>Signal timeframe asal trade (M15/H1/D1) — untuk per-TF time stop.</summary>
    public string? Timeframe { get; private set; }

    // Required for ORM/serialization
    private TradePosition() { TradeId = null!; RunId = null!; Pair = null!; Mode = null!; }

    public static TradePosition CreateSimulated(
        string tradeId,
        string runId,
        string pair,
        SignalDirection direction,
        decimal entry,
        decimal stopLoss,
        decimal takeProfit,
        decimal lotSize,
        decimal riskAmount,
        decimal potentialProfit,
        decimal riskReward,
        string? timeframe = null)
    {
        return new TradePosition
        {
            TradeId = tradeId,
            RunId = runId,
            Status = TradeStatus.ACTIVE,
            Pair = pair,
            Direction = direction,
            Entry = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            LotSize = lotSize,
            RiskAmount = riskAmount,
            PotentialProfit = potentialProfit,
            RiskReward = riskReward,
            FloatingPnl = 0m,
            FloatingPnlPips = 0,
            OpenedAt = DateTimeOffset.UtcNow,
            Mode = "SIMULATION",
            Timeframe = timeframe
        };
    }

    public static TradePosition CreateBrokerExecuted(
        string tradeId,
        string runId,
        string pair,
        SignalDirection direction,
        decimal entry,
        decimal stopLoss,
        decimal takeProfit,
        decimal lotSize,
        decimal riskAmount,
        decimal potentialProfit,
        decimal riskReward,
        string mode,
        string? externalTradeId,
        string? timeframe = null)
    {
        return new TradePosition
        {
            TradeId = tradeId,
            RunId = runId,
            Status = TradeStatus.ACTIVE,
            Pair = pair,
            Direction = direction,
            Entry = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            LotSize = lotSize,
            RiskAmount = riskAmount,
            PotentialProfit = potentialProfit,
            RiskReward = riskReward,
            FloatingPnl = 0m,
            FloatingPnlPips = 0,
            OpenedAt = DateTimeOffset.UtcNow,
            Mode = mode,
            ExternalTradeId = externalTradeId,
            Timeframe = timeframe
        };
    }

    public static TradePosition CreateSkipped(string tradeId, string runId, string pair, string skipReason)
    {
        return new TradePosition
        {
            TradeId = tradeId,
            RunId = runId,
            Status = TradeStatus.SKIPPED,
            Pair = pair,
            Direction = SignalDirection.HOLD,
            Entry = 0,
            StopLoss = 0,
            TakeProfit = 0,
            LotSize = 0,
            RiskAmount = 0,
            PotentialProfit = 0,
            RiskReward = 0,
            Mode = "SIMULATION",
            SkipReason = skipReason
        };
    }

    public static TradePosition CreateFromHistory(
        string tradeId,
        string runId,
        string pair,
        SignalDirection direction,
        decimal entry,
        decimal stopLoss,
        decimal takeProfit,
        decimal lotSize,
        decimal riskAmount,
        decimal potentialProfit,
        decimal riskReward,
        decimal floatingPnl,
        int floatingPnlPips,
        DateTimeOffset? openedAt,
        DateTimeOffset? closedAt,
        TradeStatus status,
        string mode,
        string? externalTradeId = null,
        string? timeframe = null)
    {
        return new TradePosition
        {
            TradeId = tradeId,
            RunId = runId,
            Status = status,
            Pair = pair,
            Direction = direction,
            Entry = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            LotSize = lotSize,
            RiskAmount = riskAmount,
            PotentialProfit = potentialProfit,
            RiskReward = riskReward,
            FloatingPnl = floatingPnl,
            FloatingPnlPips = floatingPnlPips,
            OpenedAt = openedAt,
            ClosedAt = closedAt,
            Mode = mode,
            ExternalTradeId = externalTradeId,
            Timeframe = timeframe
        };
    }

    public bool IsActive() => Status == TradeStatus.ACTIVE;

    public void CloseManually(TradeStatus outcome, decimal exitPrice)
    {
        if (Status != TradeStatus.ACTIVE) return;
        var priceDelta = Direction == SignalDirection.BUY
            ? exitPrice - Entry
            : Entry - exitPrice;
        // P&L USD = price delta × lot × contract size (100k untuk EURUSD).
        // Pip count = price delta × 10000 (1 pip = 0.0001 untuk EURUSD).
        FloatingPnlPips = (int)Math.Round(priceDelta * 10000m);
        FloatingPnl     = Math.Round(priceDelta * LotSize * 100000m, 2);
        Status   = outcome;
        ClosedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Update floating PnL langsung dari nilai broker (EA MT5).
    /// Tidak menghitung ulang dari harga — gunakan profit asli dari MIFX.
    /// </summary>
    public void UpdatePnlFromBroker(decimal profit, int pips)
    {
        if (Status != TradeStatus.ACTIVE) return;
        FloatingPnl     = profit;
        FloatingPnlPips = pips;
    }

    /// <summary>
    /// Tandai posisi sebagai ditutup oleh broker (SL/TP hit atau manual close di MT5).
    /// Dipanggil saat EA tidak lagi melaporkan posisi ini di daftar open positions.
    /// FloatingPnl dipertahankan sebagai nilai PnL terakhir yang diketahui.
    /// </summary>
    public void ClosedByBroker(TradeStatus outcome)
    {
        if (Status != TradeStatus.ACTIVE) return;
        Status   = outcome;
        ClosedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Tandai posisi sebagai ditutup oleh broker DENGAN nilai realized profit yang akurat.
    /// Dipanggil oleh EA setelah read HistoryDealGetDouble(DEAL_PROFIT) — mencakup
    /// commission + swap + actual fill price (bukan estimasi dari tick terakhir).
    ///
    /// Bisa dipanggil walaupun status sudah CLOSED — EA report adalah source of truth
    /// untuk realized P&L (race: close-market endpoint mark CLOSED dulu pakai estimasi,
    /// EA kirim actual profit setelah broker confirm). Update floating PnL + outcome
    /// menggunakan nilai realized agar dashboard akurat.
    /// </summary>
    public void ClosedByBrokerWithProfit(decimal netProfit, decimal exitPrice, DateTimeOffset closedAt)
    {
        FloatingPnl     = Math.Round(netProfit, 2);
        var pipValue = Direction == SignalDirection.BUY
            ? exitPrice - Entry
            : Entry - exitPrice;
        FloatingPnlPips = (int)Math.Round(pipValue * 10000m);
        Status   = netProfit >= 0m ? TradeStatus.CLOSED_WIN : TradeStatus.CLOSED_LOSS;
        ClosedAt = closedAt;
    }

    public void UpdateFloatingPnl(decimal currentPrice)
    {
        if (Status != TradeStatus.ACTIVE) return;

        decimal pipValue = Direction == SignalDirection.BUY
            ? currentPrice - Entry
            : Entry - currentPrice;

        FloatingPnlPips = (int)(pipValue * 10000);
        FloatingPnl = Math.Round(pipValue * LotSize * 100000 * 0.0001m, 2);

        if (Direction == SignalDirection.BUY && currentPrice <= StopLoss)
            Close(TradeStatus.CLOSED_LOSS);
        else if (Direction == SignalDirection.BUY && currentPrice >= TakeProfit)
            Close(TradeStatus.CLOSED_WIN);
        else if (Direction == SignalDirection.SELL && currentPrice >= StopLoss)
            Close(TradeStatus.CLOSED_LOSS);
        else if (Direction == SignalDirection.SELL && currentPrice <= TakeProfit)
            Close(TradeStatus.CLOSED_WIN);
    }

    private void Close(TradeStatus outcome)
    {
        Status = outcome;
        ClosedAt = DateTimeOffset.UtcNow;
    }
}
