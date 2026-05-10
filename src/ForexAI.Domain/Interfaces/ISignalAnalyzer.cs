using ForexAI.Domain.Entities;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

public interface ISignalAnalyzer
{
    Task<TradeSignal> AnalyzeAsync(MarketSnapshot snapshot);
}
