using ForexAI.Domain.ValueObjects;

namespace ForexAI.Domain.Interfaces;

public interface IBrokerService
{
    bool IsLive { get; }
    Task<BrokerAccountInfo> GetAccountAsync();
    Task<string?> PlaceOrderAsync(BrokerOrderRequest request);
}
