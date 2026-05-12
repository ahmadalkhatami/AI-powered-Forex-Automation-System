using ForexAI.Domain.Entities;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

public interface ITradePositionRepository
{
    Task<TradePosition?> GetActiveByPairAsync(string pair);
    Task<IReadOnlyList<TradePosition>> GetOpenPositionsAsync();
    Task<IReadOnlyList<TradePosition>> GetAllAsync();
    Task SaveAsync(TradePosition position);
    Task<int> CountOpenPositionsAsync();

    /// <summary>
    /// Hitung total risk amount yang sudah dipakai hari ini (UTC day) — untuk daily risk cap.
    /// Penjumlahan RiskAmount semua TradePosition (non-SKIPPED) yang OpenedAt-nya pada UTC day yang sama dengan asOfUtc.
    /// </summary>
    Task<DailyRiskUsage> GetDailyRiskUsageAsync(DateTimeOffset asOfUtc);
}
