using ForexAI.Domain.ValueObjects;
using ForexAI.Domain.Entities;

namespace ForexAI.Domain.Interfaces;

public interface IBrokerService
{
    bool IsLive { get; }
    Task<BrokerAccountInfo> GetAccountAsync();
    Task<string?> PlaceOrderAsync(BrokerOrderRequest request);
    Task<BrokerExecutionResult> ClosePositionAsync(TradePosition position, CancellationToken cancellationToken = default);
}
