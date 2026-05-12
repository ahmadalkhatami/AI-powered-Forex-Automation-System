using ForexAI.API.Models;
using ForexAI.Infrastructure.Services.Exness;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

/// <summary>
/// The local HTTP bridge between the C# backend and the MT5 Expert Advisor.
/// The MT5 EA polls /api/mt5/poll every second and POSTs results to /api/mt5/callback.
/// </summary>
[ApiController]
[Route("api/mt5")]
public class Mt5BridgeController : ControllerBase
{
    private readonly Mt5CommandBus _bus;
    private readonly ILogger<Mt5BridgeController> _logger;

    public Mt5BridgeController(Mt5CommandBus bus, ILogger<Mt5BridgeController> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    /// MT5 EA calls this every ~1 second to pick up pending commands.
    /// Returns IDLE when nothing is waiting.
    /// </summary>
    [HttpGet("poll")]
    public IActionResult Poll()
    {
        var cmd = _bus.Dequeue();
        if (cmd is null)
        {
            return Ok(new Mt5PollResponse { CommandType = "IDLE" });
        }

        _logger.LogInformation("[MT5 POLL] Dispatching command {Type} (id={Id})", cmd.Value.CommandType, cmd.Value.CommandId);

        var response = new Mt5PollResponse
        {
            CommandId = cmd.Value.CommandId,
            CommandType = cmd.Value.CommandType
        };

        // Map the typed payload into the response model
        if (cmd.Value.Payload is { } p)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(p);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Symbol", out var sym)) response.Symbol = sym.GetString();
            if (root.TryGetProperty("Timeframe", out var tf)) response.Timeframe = tf.GetString();
            if (root.TryGetProperty("Count", out var cnt)) response.CandleCount = cnt.GetInt32();
            if (root.TryGetProperty("Direction", out var dir)) response.TradeDirection = dir.GetString();
            if (root.TryGetProperty("LotSize", out var lot)) response.LotSize = lot.GetDouble();
            if (root.TryGetProperty("StopLoss", out var sl)) response.StopLoss = sl.GetDouble();
            if (root.TryGetProperty("TakeProfit", out var tp)) response.TakeProfit = tp.GetDouble();
        }

        return Ok(response);
    }

    /// <summary>
    /// MT5 EA posts the execution result (candle data or trade result) here.
    /// </summary>
    [HttpPost("callback")]
    public IActionResult Callback([FromBody] Mt5CallbackRequest req)
    {
        _logger.LogInformation("[MT5 CALLBACK] Received {Type} (id={Id}) Success={Ok}", req.CommandType, req.CommandId, req.Success);

        var payload = new ForexAI.Infrastructure.Services.Exness.Mt5CallbackPayload(
            req.Success,
            req.ErrorMessage,
            req.AccountEquity,
            req.AccountBalance,
            req.AccountMarginUsed,
            req.AccountMarginFree,
            req.OpenPositionCount,
            req.Candles?.Select(c => new ForexAI.Infrastructure.Services.Exness.Mt5CandleRow(c.Time, c.Open, c.High, c.Low, c.Close, c.Volume)).ToList() ?? new(),
            req.Symbol,
            req.Timeframe,
            req.BrokerOrderId,
            req.ExecutedPrice
        );

        var resolved = _bus.Complete(req.CommandId, payload);
        if (!resolved)
        {
            _logger.LogWarning("Received callback for unknown CommandId={Id} – ignored.", req.CommandId);
            return NotFound(new { message = $"Unknown CommandId: {req.CommandId}" });
        }

        return Ok(new { message = "OK" });
    }

    /// <summary>
    /// Simple health-check endpoint – the EA can call this on startup to confirm the server is alive.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "alive", serverTime = DateTimeOffset.UtcNow });
}
