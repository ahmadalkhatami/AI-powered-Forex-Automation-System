using System.Globalization;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Mifx;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services;

public record BacktestParams(
    string  Pair             = "EURUSD",
    string  Timeframe        = "M15",
    decimal StartingEquity   = 1000m,
    int     MaxBarsPerTrade  = 96,        // 1 day untuk M15
    decimal MinConfidence    = 0m,        // 0–1 — konsensus indikator (consistency)
    int     MinConfluence    = 0,         // 0–100 — kualitas weighted score (strength)
    bool    BlockHold        = true);     // Skip signals HOLD

public record BacktestTrade(
    long     EntryTime,
    decimal  EntryPrice,
    string   Direction,
    decimal  StopLoss,
    decimal  TakeProfit,
    decimal  LotSize,
    long?    ExitTime,
    decimal? ExitPrice,
    string   Status,            // "WIN" | "LOSS" | "TIMEOUT"
    decimal  Pnl,
    int      Pips,
    decimal  Confidence,
    int      Confluence,        // 0-100, quality weighted score
    int      BarsHeld);

public record EquityPoint(long Time, decimal Equity);

public record BacktestResult(
    string                Pair,
    string                Timeframe,
    int                   CandleCount,
    int                   BacktestBars,
    int                   TotalTrades,
    int                   Wins,
    int                   Losses,
    int                   Timeouts,
    decimal               StartingEquity,
    decimal               FinalEquity,
    decimal               NetPnl,
    decimal               GrossWin,
    decimal               GrossLoss,
    decimal               ProfitFactor,
    decimal               Expectancy,
    decimal               WinRate,
    decimal               MaxDrawdownPct,
    int                   MaxConsecutiveWins,
    int                   MaxConsecutiveLosses,
    List<BacktestTrade>   Trades,
    List<EquityPoint>     EquityCurve);

/// <summary>
/// Backtest harness — replay historical candles dari MifxCandleFeed melalui pipeline yang sama
/// seperti live (LiveSignalAnalyzer), simulate fill di next-bar open, scan SL/TP hit di bar berikutnya.
///
/// <para>Scope: hanya pakai candle yang sudah ter-cache di MifxCandleFeed (max ~200 bar / timeframe).
/// Untuk historis lebih panjang perlu ingest dari MT5 history (out of scope MVP).</para>
/// </summary>
public class BacktestRunner
{
    private readonly MifxCandleFeed _feed;
    private readonly ILogger<BacktestRunner> _logger;

    public BacktestRunner(MifxCandleFeed feed, ILogger<BacktestRunner> logger)
    {
        _feed   = feed;
        _logger = logger;
    }

    public async Task<BacktestResult> RunAsync(BacktestParams p)
    {
        var candles = _feed.Get(p.Pair, p.Timeframe, 1000);
        if (candles.Count < 51)
            throw new InvalidOperationException(
                $"Tidak cukup candle untuk backtest: {candles.Count} (butuh ≥ 51 untuk warmup MA50). " +
                $"Pastikan EA push candle untuk {p.Pair} {p.Timeframe} dulu.");

        var brokerStub  = new BacktestBrokerStub(p.StartingEquity);
        // Stub sistem state: cooldown disabled untuk backtest (kita backtest fresh tanpa loss history).
        var stateStub   = new BacktestSystemStateStub();
        var modeStub    = new BacktestModeServiceStub();
        var analyzer    = new LiveSignalAnalyzer(brokerStub, stateStub, modeStub);

        var trades        = new List<BacktestTrade>();
        var equityCurve   = new List<EquityPoint> { new(candles[50].Time, p.StartingEquity) };
        decimal equity    = p.StartingEquity;
        decimal peak      = p.StartingEquity;
        decimal maxDdPct  = 0m;
        int     skipUntil = 0;  // skip iterations sampai bar ini (selama posisi simulasi masih terbuka)

        for (int i = 50; i < candles.Count - 1; i++)
        {
            if (i < skipUntil) continue;  // serialize: tidak overlap trades

            var snap   = BuildSnapshot(candles, i, p.Pair, p.Timeframe);
            var signal = await analyzer.AnalyzeAsync(snap);

            if (p.BlockHold && signal.Signal == SignalDirection.HOLD) continue;
            if (signal.ConfidenceScore < p.MinConfidence) continue;
            if (signal.ConfluenceScore < p.MinConfluence) continue;
            if (signal.Signal != SignalDirection.BUY && signal.Signal != SignalDirection.SELL) continue;

            // Fill di open bar berikutnya
            var entryBar  = candles[i + 1];
            var entry     = entryBar.Open;
            var direction = signal.Signal == SignalDirection.BUY ? "BUY" : "SELL";

            // Recalc SL/TP relative ke fill price (LiveSignalAnalyzer pakai snap.CurrentPrice)
            decimal slDelta = Math.Abs(signal.Parameters.StopLoss   - signal.Parameters.Entry);
            decimal tpDelta = Math.Abs(signal.Parameters.TakeProfit - signal.Parameters.Entry);
            decimal sl, tp;
            if (direction == "BUY")
            {
                sl = entry - slDelta;
                tp = entry + tpDelta;
            }
            else
            {
                sl = entry + slDelta;
                tp = entry - tpDelta;
            }

            var exit = SimulateExit(candles, startIdx: i + 1, entry, sl, tp,
                                    isBuy: direction == "BUY", maxBars: p.MaxBarsPerTrade);

            decimal pipsRaw = direction == "BUY"
                ? exit.Price - entry
                : entry      - exit.Price;
            int pips = (int)Math.Round(pipsRaw * 10000m);
            decimal lot = signal.Parameters.LotSize;
            decimal pnl = Math.Round(pips * lot * 10m / 1m, 2);  // pips × lot × $10/pip

            trades.Add(new BacktestTrade(
                EntryTime:  entryBar.Time,
                EntryPrice: entry,
                Direction:  direction,
                StopLoss:   sl,
                TakeProfit: tp,
                LotSize:    lot,
                ExitTime:   exit.Time,
                ExitPrice:  exit.Price,
                Status:     exit.Status,
                Pnl:        pnl,
                Pips:       pips,
                Confidence: signal.ConfidenceScore,
                Confluence: signal.ConfluenceScore,
                BarsHeld:   exit.BarsHeld));

            equity += pnl;
            if (equity > peak) peak = equity;
            decimal dd = peak > 0m ? (peak - equity) / peak : 0m;
            if (dd > maxDdPct) maxDdPct = dd;

            equityCurve.Add(new EquityPoint(exit.Time, equity));
            brokerStub.SetEquity(equity);

            // Serialize: tidak overlap posisi (mirip 1 trade-aktif rule live)
            skipUntil = i + 1 + exit.BarsHeld;
        }

        var wins      = trades.Count(t => t.Status == "WIN");
        var losses    = trades.Count(t => t.Status == "LOSS");
        var timeouts  = trades.Count(t => t.Status == "TIMEOUT");
        var grossWin  = trades.Where(t => t.Pnl > 0m).Sum(t => t.Pnl);
        var grossLoss = Math.Abs(trades.Where(t => t.Pnl < 0m).Sum(t => t.Pnl));
        var netPnl    = equity - p.StartingEquity;
        var pf        = grossLoss > 0m ? Math.Round(grossWin / grossLoss, 2)
                                       : (grossWin > 0m ? 999m : 0m);
        var winRate   = trades.Count > 0 ? (decimal)wins / trades.Count : 0m;
        var expectancy= trades.Count > 0 ? Math.Round(netPnl / trades.Count, 2) : 0m;

        var (maxConsecWins, maxConsecLosses) = ComputeMaxStreaks(trades);

        _logger.LogInformation(
            "Backtest selesai: {Pair} {TF} bars={Bars} trades={N} win={W} loss={L} PF={PF} netPnl=${NP:F2}",
            p.Pair, p.Timeframe, candles.Count, trades.Count, wins, losses, pf, netPnl);

        return new BacktestResult(
            Pair:               p.Pair,
            Timeframe:          p.Timeframe,
            CandleCount:        candles.Count,
            BacktestBars:       candles.Count - 50,
            TotalTrades:        trades.Count,
            Wins:               wins,
            Losses:             losses,
            Timeouts:           timeouts,
            StartingEquity:     p.StartingEquity,
            FinalEquity:        Math.Round(equity, 2),
            NetPnl:             Math.Round(netPnl, 2),
            GrossWin:           Math.Round(grossWin, 2),
            GrossLoss:          Math.Round(grossLoss, 2),
            ProfitFactor:       pf,
            Expectancy:         expectancy,
            WinRate:            Math.Round(winRate, 4),
            MaxDrawdownPct:     Math.Round(maxDdPct, 4),
            MaxConsecutiveWins: maxConsecWins,
            MaxConsecutiveLosses: maxConsecLosses,
            Trades:             trades,
            EquityCurve:        equityCurve);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private record ExitInfo(string Status, decimal Price, long Time, int BarsHeld);

    private static ExitInfo SimulateExit(
        IReadOnlyList<CandleBar> candles, int startIdx,
        decimal entry, decimal sl, decimal tp, bool isBuy, int maxBars)
    {
        int endIdx = Math.Min(startIdx + maxBars, candles.Count);
        for (int k = startIdx; k < endIdx; k++)
        {
            var bar = candles[k];
            bool slHit, tpHit;
            if (isBuy)
            {
                slHit = bar.Low  <= sl;
                tpHit = bar.High >= tp;
            }
            else
            {
                slHit = bar.High >= sl;
                tpHit = bar.Low  <= tp;
            }
            int bars = k - startIdx + 1;
            // Konservatif: kalau SL & TP keduanya touched di bar yang sama, asumsikan SL hit dulu
            if (slHit) return new ExitInfo("LOSS", sl, bar.Time, bars);
            if (tpHit) return new ExitInfo("WIN",  tp, bar.Time, bars);
        }
        // Timeout — close di candle terakhir yang ter-evaluasi
        int lastIdx = Math.Min(endIdx - 1, candles.Count - 1);
        var last = candles[lastIdx];
        return new ExitInfo("TIMEOUT", last.Close, last.Time, lastIdx - startIdx + 1);
    }

    private static MarketSnapshot BuildSnapshot(
        IReadOnlyList<CandleBar> candles, int i, string pair, string tf)
    {
        var window  = candles.Take(i + 1).ToList();
        var closes  = window.Select(c => c.Close).ToArray();
        var highs   = window.Select(c => c.High).ToArray();
        var lows    = window.Select(c => c.Low).ToArray();

        decimal ma20 = closes[^20..].Average();
        decimal ma50 = closes.Length >= 50 ? closes[^50..].Average() : ma20;
        decimal rsi  = ComputeRsi(closes, 14);
        string  rsiDir = closes[^1] > closes[^2] ? "rising" : "falling";

        int lookback = Math.Min(20, closes.Length);
        decimal support    = lows.TakeLast(lookback).Min();
        decimal resistance = highs.TakeLast(lookback).Max();

        decimal atr = ComputeAtr(window, 14);

        return new MarketSnapshot(
            Pair:           pair,
            Timeframe:      tf,
            CurrentPrice:   candles[i].Close,
            MA20_M15:       ma20,
            MA50_M15:       ma50,
            MA20_H1:        ma20,    // simplification: backtest hanya 1 timeframe
            MA50_H1:        ma50,
            RSI14:          rsi,
            RSIDirection:   rsiDir,
            SupportZone:    support.ToString("F5", CultureInfo.InvariantCulture),
            ResistanceZone: resistance.ToString("F5", CultureInfo.InvariantCulture),
            Session:        "Backtest",
            CapturedAt:     DateTimeOffset.FromUnixTimeSeconds(candles[i].Time),
            ATR14:          atr,
            ADX14:          25m,         // default Trending agar tidak di-block regime filter
            Regime:         "Trending");
    }

    private static decimal ComputeRsi(decimal[] closes, int period)
    {
        if (closes.Length < period + 1) return 50m;
        var changes = new decimal[closes.Length - 1];
        for (int j = 0; j < changes.Length; j++) changes[j] = closes[j + 1] - closes[j];
        decimal avgGain = changes[..period].Where(c => c > 0).DefaultIfEmpty(0m).Average();
        decimal avgLoss = changes[..period].Where(c => c < 0).Select(c => -c).DefaultIfEmpty(0m).Average();
        for (int j = period; j < changes.Length; j++)
        {
            avgGain = (avgGain * (period - 1) + Math.Max(changes[j], 0m)) / period;
            avgLoss = (avgLoss * (period - 1) + Math.Abs(Math.Min(changes[j], 0m))) / period;
        }
        if (avgLoss == 0m) return 100m;
        return Math.Round(100m - 100m / (1m + avgGain / avgLoss), 2);
    }

    private static decimal ComputeAtr(IReadOnlyList<CandleBar> window, int period)
    {
        if (window.Count < period + 1) return 0m;
        decimal atrSum = 0m;
        for (int j = window.Count - period; j < window.Count; j++)
        {
            decimal h  = window[j].High;
            decimal l  = window[j].Low;
            decimal pc = window[j - 1].Close;
            decimal tr = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
            atrSum += tr;
        }
        return Math.Round(atrSum / period, 5);
    }

    private static (int MaxWins, int MaxLosses) ComputeMaxStreaks(List<BacktestTrade> trades)
    {
        int curW = 0, curL = 0, maxW = 0, maxL = 0;
        foreach (var t in trades)
        {
            if (t.Status == "WIN")  { curW++; curL = 0; if (curW > maxW) maxW = curW; }
            else if (t.Status == "LOSS") { curL++; curW = 0; if (curL > maxL) maxL = curL; }
            else                          { curW = 0; curL = 0; }
        }
        return (maxW, maxL);
    }
}

/// <summary>IBrokerService stub untuk backtest — return fixed equity, semua execute path throw.</summary>
internal class BacktestBrokerStub : IBrokerService
{
    private decimal _equity;
    public BacktestBrokerStub(decimal startingEquity) => _equity = startingEquity;
    public void SetEquity(decimal e) => _equity = e;
    public bool IsLive => false;
    public Task<BrokerAccountInfo> GetAccountAsync()
        => Task.FromResult(new BrokerAccountInfo(_equity, _equity, 0m));
    public Task<BrokerOrderResult> PlaceOrderAsync(BrokerOrderRequest request)
        => throw new NotSupportedException("Backtest mode — no real orders");
    public Task<BrokerExecutionResult> ClosePositionAsync(TradePosition position, CancellationToken ct = default)
        => throw new NotSupportedException("Backtest mode — no real closes");
}

/// <summary>
/// ISystemStateService stub untuk backtest — selalu non-halt + cooldown disabled.
/// Tujuan: backtest harness uji analyzer dalam isolasi, tanpa kontaminasi state runtime production.
/// </summary>
internal class BacktestSystemStateStub : ISystemStateService
{
    public bool IsHalted => false;
    public string? HaltReason => null;
    public DateTimeOffset? HaltedAt => null;
    public decimal MaxSpreadPips => 2.5m;
    public int MaxConsecutiveLosses => 3;
    public int MaxHoldingMinutes => 360;
    public SignalDirection? LastLossDirection => null;
    public DateTimeOffset? LastLossAt => null;
    public int CooldownMinutes => 0;  // disabled untuk backtest
    public decimal NanoMaxDailyLossUsd => 0m;  // disabled untuk backtest
    public decimal NanoEquityFloorUsd  => 0m;

    public void Halt(string reason) { }
    public void Resume() { }
    public void RegisterLoss(SignalDirection direction) { }
    public bool IsInCooldown(SignalDirection direction, out int minutesRemaining) { minutesRemaining = 0; return false; }
}

/// <summary>
/// IModeService stub untuk backtest — selalu Demo mode (backtest historis tidak ada notion real/demo).
/// </summary>
internal class BacktestModeServiceStub : IModeService
{
    public TradeMode CurrentMode => TradeMode.Demo;
    public DateTimeOffset? LastReportedAt => null;
    public event EventHandler<ModeChangedEventArgs>? ModeChanged { add { } remove { } }
    public void ReportFromEa(string? accountMode) { }
}
