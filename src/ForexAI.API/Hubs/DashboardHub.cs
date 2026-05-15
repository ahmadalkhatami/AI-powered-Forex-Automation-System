using Microsoft.AspNetCore.SignalR;

namespace ForexAI.API.Hubs;

/// <summary>
/// Hub real-time untuk dashboard. Server-side broadcast saja — client tidak invoke method apa-apa.
/// Events yang di-broadcast:
///   - "tick"      → MIFX status (bid/ask/mid/spread + connected)
///   - "positions" → daftar TradePosition terbaru
///   - "account"   → AccountHealthResult terbaru (equity, drawdown, daily risk)
/// </summary>
public class DashboardHub : Hub
{
    // Client cuma subscribe — tidak ada method server-callable
}
