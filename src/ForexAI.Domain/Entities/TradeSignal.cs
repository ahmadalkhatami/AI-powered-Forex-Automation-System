using ForexAI.Domain.Enums;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Entities;

public class TradeSignal
{
    public Guid Id { get; private set; }
    public string RunId { get; private set; }
    public string Pair { get; private set; }
    public string Timeframe { get; private set; }
    public SignalDirection Signal { get; private set; }
    public int ConfluenceScore { get; private set; }
    public decimal ConfidenceScore { get; private set; }
    public MarketSnapshot Snapshot { get; private set; }
    public TrendAnalysis Trend { get; private set; }
    public MomentumAnalysis Momentum { get; private set; }
    public StructureAnalysis Structure { get; private set; }
    public TradeParameters Parameters { get; private set; }
    public IReadOnlyList<string> Warnings { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    // Required for ORM/serialization
    private TradeSignal() { RunId = null!; Pair = null!; Timeframe = null!; Snapshot = null!; Trend = null!; Momentum = null!; Structure = null!; Parameters = null!; Warnings = null!; }

    public TradeSignal(
        string runId,
        string pair,
        string timeframe,
        SignalDirection signal,
        int confluenceScore,
        decimal confidenceScore,
        MarketSnapshot snapshot,
        TrendAnalysis trend,
        MomentumAnalysis momentum,
        StructureAnalysis structure,
        TradeParameters parameters,
        IReadOnlyList<string> warnings)
    {
        Id = Guid.NewGuid();
        RunId = runId;
        Pair = pair;
        Timeframe = timeframe;
        Signal = signal;
        ConfluenceScore = confluenceScore;
        ConfidenceScore = confidenceScore;
        Snapshot = snapshot;
        Trend = trend;
        Momentum = momentum;
        Structure = structure;
        Parameters = parameters;
        Warnings = warnings;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public static TradeSignal CreateFromHistory(
        Guid id,
        string runId,
        string pair,
        string timeframe,
        SignalDirection signal,
        int confluenceScore,
        decimal confidenceScore,
        MarketSnapshot snapshot,
        TrendAnalysis trend,
        MomentumAnalysis momentum,
        StructureAnalysis structure,
        TradeParameters parameters,
        IReadOnlyList<string> warnings,
        DateTimeOffset timestamp)
    {
        return new TradeSignal
        {
            Id = id,
            RunId = runId,
            Pair = pair,
            Timeframe = timeframe,
            Signal = signal,
            ConfluenceScore = confluenceScore,
            ConfidenceScore = confidenceScore,
            Snapshot = snapshot,
            Trend = trend,
            Momentum = momentum,
            Structure = structure,
            Parameters = parameters,
            Warnings = warnings,
            Timestamp = timestamp
        };
    }

    public bool IsActionable() =>
        Signal != SignalDirection.HOLD && ConfidenceScore >= 0.60m;
}
