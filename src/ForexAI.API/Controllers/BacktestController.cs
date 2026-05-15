using ForexAI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

public record BacktestRunRequest(
    string  Pair             = "EURUSD",
    string  Timeframe        = "M15",
    decimal StartingEquity   = 1000m,
    int     MaxBarsPerTrade  = 96,
    decimal MinConfidence    = 0m,
    int     MinConfluence    = 0,
    bool    BlockHold        = true);

[ApiController]
[Route("api/backtest")]
public class BacktestController : ControllerBase
{
    private readonly BacktestRunner _runner;

    public BacktestController(BacktestRunner runner) => _runner = runner;

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] BacktestRunRequest req)
    {
        var result = await _runner.RunAsync(new BacktestParams(
            Pair:            req.Pair,
            Timeframe:       req.Timeframe,
            StartingEquity:  req.StartingEquity,
            MaxBarsPerTrade: req.MaxBarsPerTrade,
            MinConfidence:   req.MinConfidence,
            MinConfluence:   req.MinConfluence,
            BlockHold:       req.BlockHold));
        return Ok(result);
    }
}
