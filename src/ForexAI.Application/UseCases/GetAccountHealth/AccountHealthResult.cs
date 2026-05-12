namespace ForexAI.Application.UseCases.GetAccountHealth;

public record AccountHealthResult(
    decimal Equity,
    decimal PeakEquity,
    decimal RealizedEquity,
    decimal UnrealizedPnl,
    decimal DrawdownPct,
    int OpenPositions,
    int MaxPositions,
    int TotalTrades,
    decimal WinRate,
    /// <summary>"LIVE" = data dari akun broker nyata, "SIMULATION" = hitung dari trade lokal</summary>
    string Source = "SIMULATION"
);
