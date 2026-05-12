namespace ForexAI.Application.UseCases.GetAccountHealth;

public record AccountHealthResult(
    decimal Equity,
    decimal PeakEquity,
    decimal RealizedEquity,
    decimal UnrealizedPnl,
    decimal DrawdownPct,
    int     OpenPositions,
    int     MaxPositions,
    int     TotalTrades,
    decimal WinRate,
    /// <summary>"LIVE" = data dari akun broker nyata, "SIMULATION" = hitung dari trade lokal</summary>
    string  Source = "SIMULATION",

    // ── Risk tier + Daily risk cap (Sprint 1 item 1) ────────────────────────
    /// <summary>Nama tier risk: "starter" / "growth" / "stable" / "scaled"</summary>
    string  RiskTier             = "starter",
    /// <summary>% risk per trade untuk tier ini (0.020 = 2%)</summary>
    decimal RiskPerTradePct      = 0.020m,
    /// <summary>% daily cap untuk tier ini (0.060 = 6%)</summary>
    decimal DailyCapPct          = 0.060m,
    /// <summary>Max jumlah trade per hari untuk tier ini</summary>
    int     MaxDailyTrades       = 3,
    /// <summary>Total risk USD yang sudah dipakai hari ini (UTC day)</summary>
    decimal DailyRiskUsedUsd     = 0m,
    /// <summary>Jumlah trade yang sudah dibuka hari ini</summary>
    int     TradesOpenedToday    = 0,
    /// <summary>% utilisasi daily cap saat ini (0.0–1.0+) — &gt;1 berarti cap sudah terlewat</summary>
    decimal DailyCapUtilization  = 0m
);
