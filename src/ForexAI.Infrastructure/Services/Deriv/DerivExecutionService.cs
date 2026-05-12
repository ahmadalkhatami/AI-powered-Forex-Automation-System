using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services.Deriv;

public class DerivExecutionService : ITradeExecutionService
{
    private readonly DerivWebSocketClient _client;
    private readonly ILogger<DerivExecutionService> _logger;

    public DerivExecutionService(DerivWebSocketClient client, ILogger<DerivExecutionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<BrokerAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Deriv] Fetching account balance...");

        var resp = await _client.SendAsync(new { balance = 1, account = "current" }, cancellationToken);

        if (resp.TryGetProperty("error", out var err))
            throw new Exception($"Deriv balance error: {err.GetProperty("message").GetString()}");

        decimal balance = 0;
        string accountId = "DERIV";

        if (resp.TryGetProperty("balance", out var bal))
        {
            balance = (decimal)bal.GetProperty("balance").GetDouble();
            accountId = bal.GetProperty("loginid").GetString() ?? "DERIV";
        }

        return new BrokerAccountStatus(
            accountId,
            balance,
            balance,
            0m,
            balance,
            0
        );
    }

    public async Task<BrokerExecutionResult> ExecuteOrderAsync(
        string pair, string direction, decimal lotSize,
        decimal? stopLoss, decimal? takeProfit,
        CancellationToken cancellationToken = default)
    {
        var symbol = ToDerivSymbol(pair);

        // Deriv uses Multiplier contracts as closest to CFD forex
        // MULTUP = BUY (price goes up), MULTDOWN = SELL (price goes down)
        var contractType = direction.ToUpper() == "BUY" ? "MULTUP" : "MULTDOWN";

        // Convert lot size to USD stake amount (1 lot ≈ $10 stake for demo purposes)
        var stakeAmount = Math.Max(1.0, (double)(lotSize * 10));

        _logger.LogInformation("[Deriv] Placing {Type} on {Symbol} stake={Stake}", contractType, symbol, stakeAmount);

        // Step 1: Get a proposal first
        var proposalResp = await _client.SendAsync(new
        {
            proposal = 1,
            amount = stakeAmount,
            basis = "stake",
            contract_type = contractType,
            currency = "USD",
            duration = 1,
            duration_unit = "d",   // 1 day
            multiplier = 50,        // 50x leverage
            symbol = symbol
        }, cancellationToken);

        if (proposalResp.TryGetProperty("error", out var propErr))
            return new BrokerExecutionResult(false, null, propErr.GetProperty("message").GetString(), 0m);

        if (!proposalResp.TryGetProperty("proposal", out var proposal))
            return new BrokerExecutionResult(false, null, "No proposal returned by Deriv", 0m);

        var proposalId = proposal.GetProperty("id").GetString();
        var spotPrice  = (decimal)proposal.GetProperty("spot").GetDouble();

        // Step 2: Buy the proposal
        var buyResp = await _client.SendAsync(new
        {
            buy = proposalId,
            price = stakeAmount
        }, cancellationToken);

        if (buyResp.TryGetProperty("error", out var buyErr))
            return new BrokerExecutionResult(false, null, buyErr.GetProperty("message").GetString(), 0m);

        if (!buyResp.TryGetProperty("buy", out var buy))
            return new BrokerExecutionResult(false, null, "Buy response missing from Deriv", 0m);

        var contractId    = buy.GetProperty("contract_id").GetInt64().ToString();
        var executedPrice = buy.TryGetProperty("start_time", out _) ? spotPrice
                          : (decimal)buy.GetProperty("buy_price").GetDouble();

        _logger.LogInformation("[Deriv] ✅ Contract opened. ID={ContractId} Price={Price}", contractId, executedPrice);

        return new BrokerExecutionResult(true, contractId, null, executedPrice);
    }

    private static string ToDerivSymbol(string pair)
    {
        var clean = pair.Replace("/", "").Replace("-", "").ToUpper();
        return $"frx{clean}";
    }
}
