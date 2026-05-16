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

        // Dedup: skip persist jika last signal (≤ 60 detik) punya pair+direction+conf+confluence yang sama.
        // Live data menunjukkan 47% sinyal duplikat dari rapid-click / auto-trigger.
        var last = await _signals.GetLatestAsync(signal.Pair);
        bool isDuplicate =
            last is not null
            && last.Signal == signal.Signal
            && last.ConfluenceScore == signal.ConfluenceScore
            && Math.Abs(last.ConfidenceScore - signal.ConfidenceScore) < 0.01m
            && (signal.Timestamp - last.Timestamp).TotalSeconds <= 60;

        if (isDuplicate)
        {
            // Return last signal (existing ID) supaya downstream evaluate-risk bisa load dari repo.
            // Sebelumnya kita return signal baru dengan ID fresh → repo tidak punya → 500 error.
            _logger.LogInformation("Returning cached signal (duplicate within 60s): {Signal} conf={Confidence:P0} id={Id}",
                last!.Signal, last.ConfidenceScore, last.Id);
            return last;
        }

        await _signals.SaveAsync(signal);
        _logger.LogInformation("Signal generated: {Signal} with confidence {Confidence:P0} for {Pair}",
            signal.Signal, signal.ConfidenceScore, signal.Pair);

        return signal;
    }
}
