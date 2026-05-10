using ForexAI.Domain.Enums;

namespace ForexAI.Domain.ValueObjects;

public record RiskValidation(
    string Decision,
    PositionDecision PositionDecision,
    TradeParameters? ValidatedParameters,
    IReadOnlyList<string> CautionNotes,
    IReadOnlyList<string> NoGoReasons
)
{
    public bool IsGo => Decision == "GO" || Decision == "GO_WITH_CAUTION";
}
