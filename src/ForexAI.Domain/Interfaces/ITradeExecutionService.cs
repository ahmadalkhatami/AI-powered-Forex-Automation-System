using ForexAI.Domain.Entities;

namespace ForexAI.Domain.Interfaces;

public interface ITradeExecutionService
{
    Task<BrokerAccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default);
    
    Task<BrokerExecutionResult> ExecuteOrderAsync(
        string pair, 
        string direction, 
        decimal lotSize, 
        decimal? stopLoss, 
        decimal? takeProfit, 
        CancellationToken cancellationToken = default);
}
