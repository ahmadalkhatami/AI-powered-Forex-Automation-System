using System.Net;
using System.Net.Http.Json;
using ForexAI.Integration.Dtos;
using Xunit;

namespace ForexAI.Integration;

public class PipelineIntegrationTests : IClassFixture<ForexApiFactory>
{
    private readonly HttpClient _client;

    public PipelineIntegrationTests(ForexApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullPipeline_AnalyzeEvaluateExecute_CreatesActivePosition()
    {
        // Step 1: Analyze signal
        var analyzeResp = await _client.PostAsJsonAsync("/api/signal/analyze",
            new { pair = "EURUSD", timeframe = "M15" });
        analyzeResp.EnsureSuccessStatusCode();
        var signal = await analyzeResp.Content.ReadFromJsonAsync<TradeSignalDto>();
        Assert.NotNull(signal);
        Assert.Equal("BUY", signal.Signal);

        // Step 2: Evaluate risk
        var evaluateResp = await _client.PostAsJsonAsync("/api/risk/evaluate",
            new
            {
                signalId = signal.Id,
                adjustedConfidence = 0.69m,
                totalScore = 83,
                agreementScore = 0.93m,
                finalDecision = "BUY",
                equity = 10000m,
                openPositions = 0
            });
        evaluateResp.EnsureSuccessStatusCode();
        var risk = await evaluateResp.Content.ReadFromJsonAsync<RiskValidationDto>();
        Assert.NotNull(risk);
        Assert.True(risk.IsGo, $"Expected GO/GO_WITH_CAUTION but got: {risk.Decision}");

        // Step 3: Execute trade
        var executeResp = await _client.PostAsJsonAsync("/api/trade/execute",
            new
            {
                signalId = signal.Id,
                riskValidation = risk,
                peakEquity = 10000m,
                currentEquity = 10000m,
                mode = "SIMULATION"
            });
        executeResp.EnsureSuccessStatusCode();
        var position = await executeResp.Content.ReadFromJsonAsync<TradePositionDto>();
        Assert.NotNull(position);
        Assert.Equal("ACTIVE", position.Status);

        // Step 4: Query position status
        var statusResp = await _client.GetAsync("/api/position/EURUSD");
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        var activePosition = await statusResp.Content.ReadFromJsonAsync<TradePositionDto>();
        Assert.NotNull(activePosition);
        Assert.Equal("ACTIVE", activePosition.Status);

        // Step 5: Close simulation position and verify dashboard-facing state updates immediately
        var closeResp = await _client.PostAsJsonAsync($"/api/position/{position.TradeId}/close",
            new { outcome = "WIN", exitPrice = position.Entry + 0.0010m });
        closeResp.EnsureSuccessStatusCode();
        var closedPosition = await closeResp.Content.ReadFromJsonAsync<TradePositionDto>();
        Assert.NotNull(closedPosition);
        Assert.Equal("CLOSED_WIN", closedPosition.Status);

        var allResp = await _client.GetAsync("/api/position");
        allResp.EnsureSuccessStatusCode();
        var allPositions = await allResp.Content.ReadFromJsonAsync<List<TradePositionDto>>();
        Assert.Contains(allPositions ?? new List<TradePositionDto>(), p =>
            p.TradeId == position.TradeId && p.Status == "CLOSED_WIN");
    }
}
