using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;

namespace ForexAI.Infrastructure.Broker;

public class NullBrokerService : IBrokerService
{
    public bool IsLive => false;

    public Task<BrokerAccountInfo> GetAccountAsync() =>
        Task.FromResult(new BrokerAccountInfo(0m, 0m, 0m));

    public Task<string?> PlaceOrderAsync(BrokerOrderRequest request) =>
        Task.FromResult<string?>(null);
}
