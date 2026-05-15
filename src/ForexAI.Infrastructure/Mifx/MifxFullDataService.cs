using System.Globalization;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Membangun MarketSnapshot SEPENUHNYA dari data MT5/MIFX EA — tanpa Yahoo Finance sama sekali.
/// Membutuhkan EA v1.15+ yang mengirim MA20/MA50 M15+H1, RSI14, dan Support/Resistance.
///
/// Jika EA belum v1.15 → lempar error dengan instruksi compile. Tidak ada fallback ke Yahoo.
/// </summary>
public class MifxFullDataService : IMarketDataService
{
    private readonly MifxPriceFeed _feed;
    private readonly MifxCandleFeed _candleFeed;
    private readonly ILogger<MifxFullDataService> _logger;

    public MifxFullDataService(MifxPriceFeed feed, MifxCandleFeed candleFeed, ILogger<MifxFullDataService> logger)
    {
        _feed       = feed;
        _candleFeed = candleFeed;
        _logger     = logger;
    }

    public Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var tick = _feed.Latest;

        if (!_feed.IsConnected || tick == null)
            throw new InvalidOperationException(
                "MT5 EA tidak terkoneksi. Pastikan ForexAI_Bridge EA berjalan di MT5.");

        if (!tick.HasIndicators)
            throw new InvalidOperationException(
                "EA_UPDATE_REQUIRED: ForexAI_Bridge v1.16 belum di-compile. " +
                "Buka MetaEditor di MT5 (F4), buka ForexAI_Bridge.mq5, tekan F7 untuk compile, " +
                "lalu restart EA di chart.");

        decimal atrPips = tick.ATR14.HasValue ? Math.Round(tick.ATR14.Value / 0.0001m, 1) : 0m;
        decimal adx14   = tick.ADX14 ?? 0m;
        string  regime  = DetectRegime(adx14);

        // D1 trend bias dari candle cache yang sudah di-push EA (PERIOD_D1).
        // Backend hitung SMA20 & SMA50 dari close prices D1 → tidak butuh perubahan EA.
        // Bernilai 0 jika candle D1 belum cukup (< 50 bar) → analyzer akan abaikan vote ini.
        var (ma20D1, ma50D1) = ComputeD1Mas(pair);

        _logger.LogInformation(
            "MT5 full data: {Pair} price={Price:F5} MA20M15={MA20:F5} RSI={RSI:F1} ATR={ATR:F1}pip ADX={ADX:F1} regime={Regime} D1: MA20={D1MA20:F5} MA50={D1MA50:F5}",
            tick.Pair, tick.Mid, tick.MA20_M15, tick.RSI14, atrPips, adx14, regime, ma20D1, ma50D1);

        var snapshot = new MarketSnapshot(
            Pair:           pair,
            Timeframe:      timeframe,
            CurrentPrice:   tick.Mid,
            MA20_M15:       tick.MA20_M15!.Value,
            MA50_M15:       tick.MA50_M15!.Value,
            MA20_H1:        tick.MA20_H1!.Value,
            MA50_H1:        tick.MA50_H1!.Value,
            RSI14:          tick.RSI14!.Value,
            RSIDirection:   tick.RSIDir == 1 ? "rising" : "falling",
            SupportZone:    tick.Support?.ToString("F5", CultureInfo.InvariantCulture)    ?? "",
            ResistanceZone: tick.Resistance?.ToString("F5", CultureInfo.InvariantCulture) ?? "",
            Session:        DetectSession(tick.Time),
            CapturedAt:     tick.Time,
            ATR14:          tick.ATR14 ?? 0m,
            ADX14:          adx14,
            Regime:         regime,
            MA20_D1:        ma20D1,
            MA50_D1:        ma50D1);

        return Task.FromResult(snapshot);
    }

    // Hitung SMA20 & SMA50 D1 dari candle cache (EA push setiap new D1 bar).
    // Bar paling baru di akhir list (index = Count-1). SMA pakai close price.
    private (decimal ma20, decimal ma50) ComputeD1Mas(string pair)
    {
        var bars = _candleFeed.Get(pair, "D1", 50);
        if (bars.Count < 50) return (0m, 0m);

        decimal sum20 = 0m, sum50 = 0m;
        int n = bars.Count;
        for (int i = n - 20; i < n; i++) sum20 += bars[i].Close;
        for (int i = n - 50; i < n; i++) sum50 += bars[i].Close;
        return (Math.Round(sum20 / 20m, 5), Math.Round(sum50 / 50m, 5));
    }

    /// <summary>
    /// Deteksi market regime dari ADX(14):
    /// &lt;20 = Ranging (sideway, MA kurang reliable)
    /// 20-25 = Transitional (mungkin mulai trending atau masih sideway)
    /// 25-40 = Trending (trend following optimal)
    /// &gt;40 = Volatile (trend kuat atau pre-reversal — waspada)
    /// </summary>
    private static string DetectRegime(decimal adx14)
    {
        if (adx14 <= 0m) return "Unknown";
        return adx14 switch
        {
            < 20m  => "Ranging",
            < 25m  => "Transitional",
            < 40m  => "Trending",
            _      => "Volatile"
        };
    }

    private static string DetectSession(DateTimeOffset time)
    {
        int h = time.UtcDateTime.Hour;
        bool london  = h >= 7 && h < 16;
        bool newYork = h >= 12 && h < 21;
        bool tokyo   = h >= 0 && h < 9;
        bool sydney  = h >= 21 || h < 6;

        if (london && newYork) return "London/New York";
        if (london)            return "London";
        if (newYork)           return "New York";
        if (tokyo)             return "Tokyo";
        if (sydney)            return "Sydney";
        return "Closed";
    }
}
