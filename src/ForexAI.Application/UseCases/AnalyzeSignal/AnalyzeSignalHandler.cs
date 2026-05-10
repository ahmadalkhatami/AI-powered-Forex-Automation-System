using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ForexAI.Application.UseCases.AnalyzeSignal;

public class AnalyzeSignalHandler : IRequestHandler<AnalyzeSignalCommand, TradeSignal>
{
    private readonly IMarketDataService _marketData;
    private readonly ISignalAnalyzer _analyzer;
    private readonly ISignalRepository _signals;
    private readonly ILogger<AnalyzeSignalHandler> _logger;

    public AnalyzeSignalHandler(
        IMarketDataService marketData,
        ISignalAnalyzer analyzer,
        ISignalRepository signals,
        ILogger<AnalyzeSignalHandler> logger)
    {
        _marketData = marketData;
        _analyzer = analyzer;
        _signals = signals;
        _logger = logger;
    }

    public async Task<TradeSignal> Handle(AnalyzeSignalCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Analyzing signal for {Pair} on {Timeframe}", request.Pair, request.Timeframe);

        var snapshot = await _marketData.GetSnapshotAsync(request.Pair, request.Timeframe);
        var signal = await _analyzer.AnalyzeAsync(snapshot);

        await _signals.SaveAsync(signal);

        _logger.LogInformation("Signal generated: {Signal} with confidence {Confidence:P0} for {Pair}",
            signal.Signal, signal.ConfidenceScore, signal.Pair);

        return signal;
    }
}
