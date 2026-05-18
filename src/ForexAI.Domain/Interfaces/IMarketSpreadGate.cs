namespace ForexAI.Domain.Interfaces;

/// <summary>
/// Gate sebelum execute trade: cek spread broker saat ini wajar atau spike.
/// Implementasi di Infrastructure (MifxPriceFeed) maintain rolling history.
///
/// <para>Dua check:</para>
/// <list type="bullet">
///   <item>Absolute: current spread > MaxSpreadPips (config) → reject.</item>
///   <item>Spike: current > 2.5× rolling avg (60 samples) AND > 1.5 pip → reject
///         (typically news event / liquidity drying up).</item>
/// </list>
/// </summary>
public interface IMarketSpreadGate
{
    /// <summary>Current spread (pips). 0 kalau tick belum ada.</summary>
    decimal CurrentSpreadPips { get; }

    /// <summary>Rolling average dari last 60 samples. Null kalau samples &lt; 20 (warm-up).</summary>
    decimal? RollingAvgSpreadPips { get; }

    /// <summary>True kalau current spread spike anomalous dibanding rolling avg.</summary>
    bool IsSpike(out decimal currentSpread, out decimal? rollingAvg);
}
