using ForexAI.Domain.Enums;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Mifx;

/// <summary>
/// Sinkronisasi posisi open antara EA MT5 dan DB lokal setiap tick (EA v1.18+).
///
/// <para>Dua tugas utama:</para>
/// <list type="bullet">
///   <item>Update FloatingPnl dari nilai real broker (bukan kalkulasi dari harga)</item>
///   <item>Auto-close posisi yang sudah tidak ada di EA (SL/TP hit atau manual close di MIFX)</item>
/// </list>
/// </summary>
public class MifxPositionSyncService
{
    private readonly ITradePositionRepository _repo;
    private readonly ISystemStateService _systemState;
    private readonly IBrokerService _broker;
    private readonly ILogger<MifxPositionSyncService> _logger;

    public MifxPositionSyncService(
        ITradePositionRepository repo,
        ISystemStateService systemState,
        IBrokerService broker,
        ILogger<MifxPositionSyncService> logger)
    {
        _repo        = repo;
        _systemState = systemState;
        _broker      = broker;
        _logger      = logger;
    }

    /// <param name="brokerPositions">
    /// Daftar posisi open dari EA (bisa kosong = semua sudah tutup).
    /// Caller hanya memanggil ini jika EA sudah mengirim field "positions" (tidak null).
    /// </param>
    public async Task SyncAsync(IReadOnlyList<MifxBrokerPosition> brokerPositions)
    {
        // Build lookup: MIFX ticket → broker position
        var byTicket = brokerPositions.ToDictionary(p => p.Ticket);

        var localOpen = await _repo.GetOpenPositionsAsync();
        if (localOpen.Count == 0) return;

        var toUpdate = new List<Domain.Entities.TradePosition>();

        foreach (var local in localOpen)
        {
            // Hanya sync posisi yang punya MIFX ticket (bukan simulasi)
            if (string.IsNullOrEmpty(local.ExternalTradeId)) continue;
            if (!local.ExternalTradeId.StartsWith("MIFX-", StringComparison.OrdinalIgnoreCase)) continue;

            var ticketStr = local.ExternalTradeId["MIFX-".Length..];
            if (!long.TryParse(ticketStr, out var ticket)) continue;

            if (byTicket.TryGetValue(ticket, out var bp))
            {
                // Masih open di MT5 — update floating PnL dari nilai broker langsung
                local.UpdatePnlFromBroker(bp.Profit, bp.Pips);
                toUpdate.Add(local);

                // Time stop — auto-close kalau posisi held > MaxHoldingMinutes
                if (_systemState.MaxHoldingMinutes > 0 && local.OpenedAt.HasValue)
                {
                    var ageMinutes = (DateTimeOffset.UtcNow - local.OpenedAt.Value).TotalMinutes;
                    if (ageMinutes >= _systemState.MaxHoldingMinutes)
                    {
                        _logger.LogWarning(
                            "Time stop fire: {Id} held {Age:F0} min ≥ {Max} min — close otomatis",
                            local.TradeId, ageMinutes, _systemState.MaxHoldingMinutes);
                        // Fire-and-forget close — actual close akan di-detect di sync berikutnya
                        _ = _broker.ClosePositionAsync(local);
                    }
                }
            }
            else
            {
                // Tidak ada di EA lagi → sudah ditutup (SL/TP hit atau manual)
                var outcome = local.FloatingPnl >= 0m
                    ? TradeStatus.CLOSED_WIN
                    : TradeStatus.CLOSED_LOSS;

                local.ClosedByBroker(outcome);
                toUpdate.Add(local);

                _logger.LogInformation(
                    "Position {Id} ({Pair} {Dir}) auto-closed oleh broker — " +
                    "outcome={Outcome} lastPnL=${Pnl:F2} ({Pips} pips)",
                    local.TradeId, local.Pair, local.Direction,
                    outcome, local.FloatingPnl, local.FloatingPnlPips);
            }
        }

        if (toUpdate.Count > 0)
            await _repo.SaveManyAsync(toUpdate);
    }
}
