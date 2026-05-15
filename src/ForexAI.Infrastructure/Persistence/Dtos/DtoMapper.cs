using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Persistence.Dtos;

internal static class DtoMapper
{
    public static TradePositionDto ToDto(TradePosition p) => new()
    {
        TradeId = p.TradeId,
        RunId = p.RunId,
        Status = p.Status.ToString(),
        Pair = p.Pair,
        Direction = p.Direction.ToString(),
        Entry = p.Entry,
        StopLoss = p.StopLoss,
        TakeProfit = p.TakeProfit,
        LotSize = p.LotSize,
        RiskAmount = p.RiskAmount,
        PotentialProfit = p.PotentialProfit,
        RiskReward = p.RiskReward,
        FloatingPnl = p.FloatingPnl,
        FloatingPnlPips = p.FloatingPnlPips,
        OpenedAt = p.OpenedAt,
        ClosedAt = p.ClosedAt,
        Mode = p.Mode,
        SkipReason = p.SkipReason,
        ExternalTradeId = p.ExternalTradeId
    };

    public static TradePosition ToDomain(TradePositionDto dto)
    {
        var status = Enum.Parse<TradeStatus>(dto.Status, ignoreCase: true);
        var direction = string.IsNullOrEmpty(dto.Direction)
            ? SignalDirection.HOLD
            : Enum.Parse<SignalDirection>(dto.Direction, ignoreCase: true);

        return status switch
        {
            TradeStatus.SKIPPED => TradePosition.CreateSkipped(
                dto.TradeId, dto.RunId, dto.Pair, dto.SkipReason ?? ""),

            TradeStatus.ACTIVE => TradePosition.CreateFromHistory(
                dto.TradeId, dto.RunId, dto.Pair, direction,
                dto.Entry, dto.StopLoss, dto.TakeProfit,
                dto.LotSize, dto.RiskAmount, dto.PotentialProfit, dto.RiskReward,
                dto.FloatingPnl, dto.FloatingPnlPips,
                dto.OpenedAt, dto.ClosedAt, status, dto.Mode,
                externalTradeId: dto.ExternalTradeId),

            TradeStatus.CLOSED_WIN or TradeStatus.CLOSED_LOSS => TradePosition.CreateFromHistory(
                dto.TradeId, dto.RunId, dto.Pair, direction,
                dto.Entry, dto.StopLoss, dto.TakeProfit,
                dto.LotSize, dto.RiskAmount, dto.PotentialProfit, dto.RiskReward,
                dto.FloatingPnl, dto.FloatingPnlPips,
                dto.OpenedAt, dto.ClosedAt, status, dto.Mode,
                externalTradeId: dto.ExternalTradeId),

            _ => TradePosition.CreateSkipped(dto.TradeId, dto.RunId, dto.Pair, "unknown status")
        };
    }

    public static TradeSignalDto ToDto(TradeSignal s) => new()
    {
        Id = s.Id,
        RunId = s.RunId,
        Pair = s.Pair,
        Timeframe = s.Timeframe,
        Signal = s.Signal.ToString(),
        ConfluenceScore = s.ConfluenceScore,
        ConfidenceScore = s.ConfidenceScore,
        Timestamp = s.Timestamp,
        Warnings = s.Warnings.ToList(),

        SnapshotCurrentPrice = s.Snapshot.CurrentPrice,
        SnapshotMA20_M15 = s.Snapshot.MA20_M15,
        SnapshotMA50_M15 = s.Snapshot.MA50_M15,
        SnapshotMA20_H1 = s.Snapshot.MA20_H1,
        SnapshotMA50_H1 = s.Snapshot.MA50_H1,
        SnapshotMA20_D1 = s.Snapshot.MA20_D1,
        SnapshotMA50_D1 = s.Snapshot.MA50_D1,
        SnapshotRSI14 = s.Snapshot.RSI14,
        SnapshotRSIDirection = s.Snapshot.RSIDirection,
        SnapshotSupportZone = s.Snapshot.SupportZone,
        SnapshotResistanceZone = s.Snapshot.ResistanceZone,
        SnapshotSession = s.Snapshot.Session,
        SnapshotCapturedAt = s.Snapshot.CapturedAt,
        SnapshotATR14 = s.Snapshot.ATR14,
        SnapshotADX14 = s.Snapshot.ADX14,
        SnapshotRegime = s.Snapshot.Regime,

        TrendBias = s.Trend.Bias,
        TrendStrength = s.Trend.Strength,
        TrendScore = s.Trend.Score,
        TrendHtfAligned = s.Trend.HtfAligned,
        TrendConfiguration = s.Trend.Configuration,
        TrendScoreRationale = s.Trend.ScoreRationale,

        MomentumRSIValue = s.Momentum.RSIValue,
        MomentumRSIDirection = s.Momentum.RSIDirection,
        MomentumZone = s.Momentum.Zone,
        MomentumScore = s.Momentum.Score,
        MomentumScoreRationale = s.Momentum.ScoreRationale,
        MomentumDivergence = s.Momentum.Divergence,

        StructureNearestSupport = s.Structure.NearestSupport,
        StructureNearestResistance = s.Structure.NearestResistance,
        StructureScore = s.Structure.Score,
        StructureScoreRationale = s.Structure.ScoreRationale,
        StructureCandleConfirmed = s.Structure.CandleConfirmed,
        StructureCandlePattern = s.Structure.CandlePattern,
        StructurePricePosition = s.Structure.PricePosition,

        ParamsEntry = s.Parameters.Entry,
        ParamsStopLoss = s.Parameters.StopLoss,
        ParamsStopLossPips = s.Parameters.StopLossPips,
        ParamsTakeProfit = s.Parameters.TakeProfit,
        ParamsTakeProfitPips = s.Parameters.TakeProfitPips,
        ParamsLotSize = s.Parameters.LotSize,
        ParamsRiskAmount = s.Parameters.RiskAmount,
        ParamsPotentialProfit = s.Parameters.PotentialProfit,
        ParamsRiskRewardRatio = s.Parameters.RiskRewardRatio
    };

    public static TradeSignal ToDomain(TradeSignalDto dto)
    {
        var signal = Enum.Parse<SignalDirection>(dto.Signal, ignoreCase: true);

        var snapshot = new MarketSnapshot(
            dto.Pair, dto.Timeframe,
            dto.SnapshotCurrentPrice,
            dto.SnapshotMA20_M15, dto.SnapshotMA50_M15,
            dto.SnapshotMA20_H1, dto.SnapshotMA50_H1,
            dto.SnapshotRSI14, dto.SnapshotRSIDirection,
            dto.SnapshotSupportZone, dto.SnapshotResistanceZone,
            dto.SnapshotSession, dto.SnapshotCapturedAt,
            dto.SnapshotATR14, dto.SnapshotADX14,
            string.IsNullOrEmpty(dto.SnapshotRegime) ? "Unknown" : dto.SnapshotRegime,
            dto.SnapshotMA20_D1, dto.SnapshotMA50_D1);

        var trend = new TrendAnalysis(
            dto.TrendBias, dto.TrendStrength,
            dto.TrendScore, dto.TrendHtfAligned,
            dto.TrendConfiguration, dto.TrendScoreRationale);

        var momentum = new MomentumAnalysis(
            dto.MomentumRSIValue, dto.MomentumRSIDirection,
            dto.MomentumZone, dto.MomentumScore,
            dto.MomentumScoreRationale, dto.MomentumDivergence);

        var structure = new StructureAnalysis(
            dto.StructureNearestSupport, dto.StructureNearestResistance,
            dto.StructureScore, dto.StructureScoreRationale,
            dto.StructureCandleConfirmed, dto.StructureCandlePattern,
            dto.StructurePricePosition);

        var parameters = new TradeParameters(
            dto.ParamsEntry, dto.ParamsStopLoss, dto.ParamsStopLossPips,
            dto.ParamsTakeProfit, dto.ParamsTakeProfitPips,
            dto.ParamsLotSize, dto.ParamsRiskAmount,
            dto.ParamsPotentialProfit, dto.ParamsRiskRewardRatio);

        return TradeSignal.CreateFromHistory(
            dto.Id, dto.RunId, dto.Pair, dto.Timeframe,
            signal, dto.ConfluenceScore, dto.ConfidenceScore,
            snapshot, trend, momentum, structure,
            parameters, dto.Warnings.AsReadOnly(),
            dto.Timestamp);
    }
}
