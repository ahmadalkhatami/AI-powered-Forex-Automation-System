using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Menggabungkan dua sumber data:
///   1. Yahoo Finance → candle harian, MA20/MA50, RSI14 (akurat untuk analisa teknikal)
///   2. MIFX live tick → CurrentPrice dan CapturedAt real-time (dari EA MT5)
///
/// Jika MIFX tidak terkoneksi → fallback ke harga Yahoo Finance.
/// </summary>
public class MifxEnrichedDataService : IMarketDataService
{
    private readonly YahooFinanceService _yahoo;
    private readonly MifxPriceFeed _feed;
    private readonly ILogger<MifxEnrichedDataService> _logger;

    public MifxEnrichedDataService(
        YahooFinanceService yahoo,
        MifxPriceFeed feed,
        ILogger<MifxEnrichedDataService> logger)
    {
        _yahoo  = yahoo;
        _feed   = feed;
        _logger = logger;
    }

    public async Task<MarketSnapshot> GetSnapshotAsync(string pair, string timeframe)
    {
        // Ambil MA/RSI dari Yahoo Finance (daily candles — akurat, stabil)
        var snapshot = await _yahoo.GetSnapshotAsync(pair, timeframe);

        // Override dengan harga live dari MIFX EA jika terkoneksi
        var tick = _feed.Latest;
        if (_feed.IsConnected && tick != null && IsSamePair(tick.Pair, pair))
        {
            _logger.LogInformation(
                "MIFX live price override: {Pair} bid={Bid} ask={Ask} mid={Mid}",
                tick.Pair, tick.Bid, tick.Ask, tick.Mid);

            // Record 'with' expression — override hanya CurrentPrice dan CapturedAt
            snapshot = snapshot with
            {
                CurrentPrice = tick.Mid,
                CapturedAt   = tick.Time,
            };
        }
        else
        {
            _logger.LogDebug("MIFX EA tidak terkoneksi — pakai harga Yahoo Finance");
        }

        return snapshot;
    }

    private static bool IsSamePair(string mifxPair, string requestedPair)
    {
        // Normalize: "EURUSD" == "EUR/USD" == "EURUSD.i"
        static string Normalize(string p) =>
            p.Replace("/", "").Replace(".", "").ToUpperInvariant()[..6];

        return Normalize(mifxPair) == Normalize(requestedPair);
    }
}
