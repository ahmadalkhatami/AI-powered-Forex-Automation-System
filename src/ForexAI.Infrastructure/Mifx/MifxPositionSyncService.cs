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
    private readonly IModeService _mode;
    private readonly ILogger<MifxPositionSyncService> _logger;

    // Track peak R-multiple per posisi untuk breakeven + trailing logic.
    // Key: tradeId. Value: peak floating R-multiple yang pernah dicapai (>=0).
    // Tujuan: tahu kapan posisi sudah pernah menyentuh +1R / +1.5R agar kita tahu kapan
    // boleh menutup di breakeven / trail. Reset di backend restart (acceptable — conservative).
    private readonly Dictionary<string, decimal> _peakR = new();

    // Track posisi yang sudah di-trigger breakeven/trailing close — supaya tidak double-fire
    // (ClosePositionAsync fire-and-forget, kalau close masih in-flight saat sync berikutnya
    //  kita bisa fire lagi tanpa guard ini → broker error / lot mismatch).
    private readonly HashSet<string> _closingInProgress = new();

    // Tier-aware threshold: nano mode (modal kecil) lebih agresif lock profit.
    // Standard: BE @ +1R, trail @ +1.5R retrace 1R, time stop 360min (6h)
    // Nano:     BE @ +0.5R, trail @ +1R retrace 0.5R, time stop 120min (2h) — protect tiny modal
    private (decimal beTrigger, decimal trailTrigger, decimal trailGiveBack, int timeStopMin) GetThresholds()
    {
        // Best-effort: kalau gak bisa hitung tier, fallback ke default standard.
        try
        {
            var account = _broker.GetAccountAsync().GetAwaiter().GetResult();
            decimal equity = account.Balance > 0 ? account.Balance : account.Equity;
            var tier = RiskTier.FromEquity(equity, _mode.CurrentMode);
            if (tier.Name == "nano")
                return (0.5m, 1.0m, 0.5m, 120);
        }
        catch { /* fallback */ }
        return (1.0m, 1.5m, 1.0m, _systemState.MaxHoldingMinutes);
    }

    public MifxPositionSyncService(
        ITradePositionRepository repo,
        ISystemStateService systemState,
        IBrokerService broker,
        IModeService mode,
        ILogger<MifxPositionSyncService> logger)
    {
        _repo        = repo;
        _systemState = systemState;
        _broker      = broker;
        _mode        = mode;
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

        // Tier-aware thresholds untuk BE/trail/time-stop (nano mode lebih protektif)
        var (beTrigger, trailTrigger, trailGiveBack, timeStopMin) = GetThresholds();

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

                // Skip semua trigger close kalau sudah ada close request in-flight
                if (_closingInProgress.Contains(local.TradeId)) continue;

                // ── Breakeven + Trailing stop logic ────────────────────────────
                // Hitung current R-multiple = floating pips / original SL distance pips.
                // SL distance == abs(Entry - StopLoss) dalam pips.
                decimal slDistPips = Math.Abs(local.Entry - local.StopLoss) / 0.0001m;
                if (slDistPips > 0m)
                {
                    decimal currentR = local.FloatingPnlPips / slDistPips;
                    // Update peak R kalau current lebih tinggi
                    decimal peakR = _peakR.TryGetValue(local.TradeId, out var existing)
                        ? Math.Max(existing, currentR)
                        : Math.Max(currentR, 0m);
                    _peakR[local.TradeId] = peakR;

                    // Trailing trigger — setelah peak ≥ trailTrigger, close kalau retrace ≥ trailGiveBack
                    if (peakR >= trailTrigger && currentR <= peakR - trailGiveBack)
                    {
                        _logger.LogWarning(
                            "Trailing stop fire: {Id} peakR={Peak:F2} currentR={Cur:F2} (give back ≥{Give:F1}R) — close",
                            local.TradeId, peakR, currentR, trailGiveBack);
                        _closingInProgress.Add(local.TradeId);
                        _ = _broker.ClosePositionAsync(local);
                    }
                    // Breakeven trigger — setelah peak ≥ beTrigger, close kalau price reverse ke entry atau lebih buruk
                    else if (peakR >= beTrigger && currentR <= 0m)
                    {
                        _logger.LogWarning(
                            "Breakeven fire: {Id} peakR={Peak:F2} currentR={Cur:F2} — price reverse ke entry, close",
                            local.TradeId, peakR, currentR);
                        _closingInProgress.Add(local.TradeId);
                        _ = _broker.ClosePositionAsync(local);
                    }
                }

                // Time stop — auto-close kalau posisi held > timeStopMin (tier-aware)
                if (timeStopMin > 0 && local.OpenedAt.HasValue)
                {
                    var ageMinutes = (DateTimeOffset.UtcNow - local.OpenedAt.Value).TotalMinutes;
                    if (ageMinutes >= timeStopMin && !_closingInProgress.Contains(local.TradeId))
                    {
                        _logger.LogWarning(
                            "Time stop fire: {Id} held {Age:F0} min ≥ {Max} min — close otomatis",
                            local.TradeId, ageMinutes, timeStopMin);
                        _closingInProgress.Add(local.TradeId);
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

                // Posisi tutup → bersihkan tracker state
                _peakR.Remove(local.TradeId);
                _closingInProgress.Remove(local.TradeId);

                // Post-loss cooldown: register direction yang baru saja LOSS
                if (outcome == TradeStatus.CLOSED_LOSS)
                {
                    _systemState.RegisterLoss(local.Direction);
                    _logger.LogWarning(
                        "Cooldown ARMED: {Dir} di-block selama {Min} menit setelah LOSS",
                        local.Direction, _systemState.CooldownMinutes);
                }

                _logger.LogInformation(
                    "Position {Id} ({Pair} {Dir}) auto-closed oleh broker — " +
                    "outcome={Outcome} lastPnL=${Pnl:F2} ({Pips} pips)",
                    local.TradeId, local.Pair, local.Direction,
                    outcome, local.FloatingPnl, local.FloatingPnlPips);
            }
        }

        if (toUpdate.Count > 0)
            await _repo.SaveManyAsync(toUpdate);

        // Prune tracker untuk posisi yang sudah tidak open (closed via path lain spt EA closed-position controller)
        var openIds = localOpen.Select(p => p.TradeId).ToHashSet();
        foreach (var staleId in _peakR.Keys.Where(k => !openIds.Contains(k)).ToList())
        {
            _peakR.Remove(staleId);
            _closingInProgress.Remove(staleId);
        }
    }
}
