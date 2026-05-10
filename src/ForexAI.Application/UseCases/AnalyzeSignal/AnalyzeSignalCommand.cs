using ForexAI.Domain.Entities;
using MediatR;

namespace ForexAI.Application.UseCases.AnalyzeSignal;

public record AnalyzeSignalCommand(string Pair, string Timeframe) : IRequest<TradeSignal>;
