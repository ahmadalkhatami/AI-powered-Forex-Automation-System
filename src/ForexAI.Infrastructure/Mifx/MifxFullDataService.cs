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
    private readonly ILogger<MifxFullDataService> _logger;

    public MifxFullDataService(MifxPriceFeed feed, ILogger<MifxFullDataService> logger)
    {
        _feed   = feed;
        _logger = logger;
    }

    public Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        var tick = _feed.Latest;

        if (!_feed.IsConnected || tick == null)
            throw new InvalidOperationException(
                "MT5 EA tidak terkoneksi. Pastikan ForexAI_Bridge EA berjalan di MT5.");

        if (!tick.HasIndicators)
            throw new InvalidOperationException(
                "EA_UPDATE_REQUIRED: ForexAI_Bridge v1.15 belum di-compile. " +
                "Buka MetaEditor di MT5 (F4), buka ForexAI_Bridge.mq5, tekan F7 untuk compile, " +
                "lalu restart EA di chart.");

        _logger.LogInformation(
            "MT5 full data: {Pair} price={Price:F5} MA20M15={MA20:F5} RSI={RSI:F1} S={S:F5} R={R:F5}",
            tick.Pair, tick.Mid, tick.MA20_M15, tick.RSI14, tick.Support, tick.Resistance);

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
            CapturedAt:     tick.Time);

        return Task.FromResult(snapshot);
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
