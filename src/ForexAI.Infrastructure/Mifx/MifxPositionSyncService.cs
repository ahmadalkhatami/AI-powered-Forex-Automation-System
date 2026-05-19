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
    // Standard: BE @ +1R, trail @ +1.5R retrace 1R
    // Nano:     BE @ +0.5R, trail @ +1R retrace 0.5R — protect tiny modal
    // Time stop dipisah ke GetTimeStopMinutes() — per-TF, bukan global.
    private (decimal beTrigger, decimal trailTrigger, decimal trailGiveBack) GetTrailingThresholds()
    {
        try
        {
            var account = _broker.GetAccountAsync().GetAwaiter().GetResult();
            decimal equity = account.Balance > 0 ? account.Balance : account.Equity;
            var tier = RiskTier.FromEquity(equity, _mode.CurrentMode);
            if (tier.Name == "nano") return (0.5m, 1.0m, 0.5m);
        }
        catch { /* fallback */ }
        return (1.0m, 1.5m, 1.0m);
    }

    /// <summary>
    /// Optional time stop — return null kalau disabled (default).
    /// Nano tier tetap pakai cap 2h apapun setting (modal $30-60 safety).
    /// User dapat enable via Settings UI dengan set MaxHoldingMinutes > 0.
    /// </summary>
    private int? GetTimeStopMinutesOptional(string? timeframe)
    {
        // Nano tier: always cap 2h (hard safety untuk modal kecil)
        try
        {
            var account = _broker.GetAccountAsync().GetAwaiter().GetResult();
            decimal equity = account.Balance > 0 ? account.Balance : account.Equity;
            var tier = RiskTier.FromEquity(equity, _mode.CurrentMode);
            if (tier.Name == "nano") return 120;
        }
        catch { /* fallback ke user setting */ }

        // Non-nano: hanya enforce kalau user explicit set di Settings UI
        return _systemState.MaxHoldingMinutes > 0 ? _systemState.MaxHoldingMinutes : (int?)null;
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

        // Tier-aware trailing thresholds (BE/trail) — apply ke semua posisi.
        // Time stop dihitung per posisi (per-TF) di loop di bawah.
        var (beTrigger, trailTrigger, trailGiveBack) = GetTrailingThresholds();

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
                        FireCloseWithLogging(local);
                    }
                    // Breakeven trigger — setelah peak ≥ beTrigger, close kalau price reverse ke entry atau lebih buruk
                    else if (peakR >= beTrigger && currentR <= 0m)
                    {
                        _logger.LogWarning(
                            "Breakeven fire: {Id} peakR={Peak:F2} currentR={Cur:F2} — price reverse ke entry, close",
                            local.TradeId, peakR, currentR);
                        _closingInProgress.Add(local.TradeId);
                        FireCloseWithLogging(local);
                    }
                }

                // Time stop — disabled by default (user choice 2026-05-19).
                // Rasionalnya: TP/SL sudah cover exit. Hanya enable kalau user
                // explicit set _systemState.MaxHoldingMinutes > 0 via Settings UI.
                // Nano tier tetap pakai cap 2h apapun setting (modal protection).
                if (local.OpenedAt.HasValue)
                {
                    int? hardCap = GetTimeStopMinutesOptional(local.Timeframe);
                    if (hardCap.HasValue && hardCap.Value > 0)
                    {
                        var ageMinutes = (DateTimeOffset.UtcNow - local.OpenedAt.Value).TotalMinutes;
                        if (ageMinutes >= hardCap.Value && !_closingInProgress.Contains(local.TradeId))
                        {
                            _logger.LogWarning(
                                "Time stop fire: {Id} (TF={Tf}) held {Age:F0} min ≥ {Max} min — close otomatis",
                                local.TradeId, local.Timeframe ?? "?", ageMinutes, hardCap.Value);
                            _closingInProgress.Add(local.TradeId);
                            FireCloseWithLogging(local);
                        }
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

    /// <summary>
    /// Wrapper untuk async close ke broker. Tidak block sync loop, tapi LOG kalau failed
    /// (sebelumnya fire-and-forget tanpa logging = audit gap).
    /// </summary>
    private void FireCloseWithLogging(Domain.Entities.TradePosition position)
    {
        _ = HandleCloseAsync(position);
    }

    private async Task HandleCloseAsync(Domain.Entities.TradePosition position)
    {
        try
        {
            var result = await _broker.ClosePositionAsync(position);
            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "Close rejected by broker for {Id}: {Reason}",
                    position.TradeId, result.ErrorMessage ?? "unknown");
                lock (_closingInProgress) _closingInProgress.Remove(position.TradeId);
            }
            else
            {
                _logger.LogInformation(
                    "Close accepted for {Id}: executed @ {Price:F5}",
                    position.TradeId, result.ExecutedPrice);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Close failed for {Id} ({Pair} {Dir}): {Err}",
                position.TradeId, position.Pair, position.Direction, ex.Message);
            lock (_closingInProgress) _closingInProgress.Remove(position.TradeId);
        }
    }
}
