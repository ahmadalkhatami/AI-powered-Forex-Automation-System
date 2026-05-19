using ForexAI.Application.UseCases.GetAdaptiveStats;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// BackgroundService yang fire Adaptive Learning evaluation tiap N jam.
/// Pre-conditions:
/// <list type="bullet">
///   <item>Global gate OPEN (totalTradeCount ≥ 50)</item>
///   <item>Master kill switch OFF</item>
///   <item>Per-action kill switch OFF</item>
/// </list>
///
/// <para>P2b1 hanya implement <b>Action 1: Per-Regime Confidence Threshold</b>.
/// Action 2-4 (session/cooldown/pattern) di iterasi berikutnya.</para>
///
/// <para>Trigger: every 6 hours (configurable lewat <see cref="TickInterval"/>).
/// Startup delay 60s untuk avoid kena bootstrap.</para>
/// </summary>
public class AdaptiveLearningService : BackgroundService
{
    private static readonly TimeSpan TickInterval     = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay     = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ActionCooldownH  = TimeSpan.FromHours(24);
    private static readonly TimeSpan SessionSkipCool  = TimeSpan.FromDays(7);    // cooldown after skip activate

    // Per-regime threshold bounds (per roadmap § 3.1)
    private const decimal Baseline      = 0.70m;
    private const decimal BoundMin      = 0.60m;
    private const decimal BoundMax      = 0.85m;
    private const decimal AdjustStep    = 0.05m;

    // Statistical gates (per roadmap § 4)
    private const decimal RaiseUpperCap = 0.40m;  // Wilson upper < 40% → bucket truly losing
    private const decimal LowerLowerCap = 0.60m;  // Wilson lower > 60% → bucket truly winning

    // Session penalty (per roadmap § 3.2)
    private const decimal SessionPenaltyStep    = 0.05m;
    private const decimal SessionPenaltyMax     = 0.15m;
    private const decimal SessionPenaltyThresh  = 0.35m;  // WR < 35% → penalty
    private const decimal SessionSkipThresh     = 0.25m;  // WR < 25% → skip 7d
    private const int     SessionSkipMinSample  = 30;

    private readonly IServiceProvider _services;
    private readonly IAdaptiveStateService _adaptive;
    private readonly ILogger<AdaptiveLearningService> _log;

    public AdaptiveLearningService(
        IServiceProvider services,
        IAdaptiveStateService adaptive,
        ILogger<AdaptiveLearningService> log)
    {
        _services = services;
        _adaptive = adaptive;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Adaptive learning cycle failed — will retry next tick");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var state = _adaptive.Current;

        if (state.MasterDisabled)
        {
            _log.LogInformation("Adaptive cycle: master kill switch ON — skip");
            return;
        }

        // Fetch latest stats via scoped mediator
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var stats = await mediator.Send(new GetAdaptiveStatsQuery(WindowSize: 30), ct);

        if (!stats.GlobalGateOpen)
        {
            _log.LogInformation(
                "Adaptive cycle: global gate CLOSED (totalTradeCount={Total} < 50) — observe only",
                stats.TotalTradeCount);
            return;
        }

        // ── Action 1: Per-regime confidence threshold ──────────────────────
        if (!state.RegimeThresholdActionDisabled)
            EvaluateRegimeThreshold(stats, state);
        else
            _log.LogInformation("Adaptive cycle: RegimeThreshold action disabled — skip");

        // ── Action 2: Session penalty / skip ───────────────────────────────
        if (!state.SessionPenaltyActionDisabled)
            EvaluateSessionPenalty(stats, _adaptive.Current);  // re-read state setelah Action 1
        else
            _log.LogInformation("Adaptive cycle: SessionPenalty action disabled — skip");

        // P2b3+: Action 3 (cooldown), Action 4 (pattern)
    }

    /// <summary>
    /// Action 1: kalau bucket regime menunjukkan WR rendah secara statistik signifikan
    /// (Wilson upper bound &lt; 40%), naikkan threshold +0.05 (bot lebih picky).
    /// Sebaliknya WR tinggi (Wilson lower &gt; 60%) → turunkan -0.05 (bot fire lebih sering).
    /// </summary>
    private void EvaluateRegimeThreshold(AdaptiveStatsResult stats, AdaptiveState state)
    {
        foreach (var bucket in stats.ByRegime)
        {
            if (!bucket.BucketReady) continue;  // butuh ≥ 20 trade

            decimal current = state.RegimeThresholdOverride.TryGetValue(bucket.Label, out var v)
                ? v : Baseline;

            decimal? target = null;
            string reason = "";

            if (bucket.WilsonUpper95 < RaiseUpperCap && current < BoundMax)
            {
                target = Math.Min(current + AdjustStep, BoundMax);
                reason = $"Regime {bucket.Label}: WR {bucket.WinRate:P0} (Wilson 95% upper {bucket.WilsonUpper95:P0}) " +
                         $"< {RaiseUpperCap:P0}, n={bucket.Trades}, expectancy {bucket.ExpectancyR:F2}R → raise threshold +{AdjustStep:F2}";
            }
            else if (bucket.WilsonLower95 > LowerLowerCap && current > BoundMin)
            {
                target = Math.Max(current - AdjustStep, BoundMin);
                reason = $"Regime {bucket.Label}: WR {bucket.WinRate:P0} (Wilson 95% lower {bucket.WilsonLower95:P0}) " +
                         $"> {LowerLowerCap:P0}, n={bucket.Trades}, expectancy {bucket.ExpectancyR:F2}R → lower threshold -{AdjustStep:F2}";
            }

            if (target is null) continue;

            // Cooldown 24h sejak last adjust untuk bucket yang sama
            var lastAdjust = state.AuditHistory.FirstOrDefault(a =>
                a.Action == "RegimeThreshold" && a.Bucket == bucket.Label);
            if (lastAdjust != null && (DateTimeOffset.UtcNow - lastAdjust.Timestamp) < ActionCooldownH)
            {
                var minLeft = (int)(ActionCooldownH - (DateTimeOffset.UtcNow - lastAdjust.Timestamp)).TotalMinutes;
                _log.LogInformation(
                    "Adaptive RegimeThreshold {Regime}: would adjust but cooldown active ({Min} min left)",
                    bucket.Label, minLeft);
                continue;
            }

            // Apply
            var update = new AdaptiveStateUpdate(
                RegimeThresholdSet: new Dictionary<string, decimal> { [bucket.Label] = target.Value });
            var audit = new AdaptiveAuditEntry(
                Timestamp:    DateTimeOffset.UtcNow,
                Action:       "RegimeThreshold",
                Bucket:       bucket.Label,
                Parameter:    $"regimeConfidenceThreshold[{bucket.Label}]",
                FromValue:    current.ToString("F2"),
                ToValue:      target.Value.ToString("F2"),
                Reason:       reason,
                SampleSize:   bucket.Trades,
                WilsonLower:  bucket.WilsonLower95,
                WilsonUpper:  bucket.WilsonUpper95,
                ExpectancyR:  bucket.ExpectancyR,
                SnapshotId:   "");   // di-isi di Apply()

            var snapshotId = _adaptive.Apply(update, audit);
            _log.LogWarning(
                "🤖 Adaptive ACTION fired: RegimeThreshold[{Regime}] {From}→{To} — {Reason} (snapshot={Snap})",
                bucket.Label, current, target.Value, reason, snapshotId);
        }
    }

    /// <summary>
    /// Action 2: kalau session WR rendah signifikan, escalate dari penalty → skip:
    /// <list type="bullet">
    ///   <item>WR &lt; 35% &amp; n ≥ 20: penalty +5% (capped at -15%)</item>
    ///   <item>WR &lt; 25% &amp; n ≥ 30: skip session 7 hari (auto re-enable)</item>
    /// </list>
    /// Cooldown 7d setelah skip activate, 24h untuk penalty adjust.
    /// </summary>
    private void EvaluateSessionPenalty(AdaptiveStatsResult stats, AdaptiveState state)
    {
        foreach (var bucket in stats.BySession)
        {
            if (!bucket.BucketReady) continue;  // butuh ≥ 20 trade
            if (bucket.Label == "Unknown" || bucket.Label == "Closed") continue;

            // ── Skip escalation: very low WR + larger sample ────────────────
            if (bucket.WilsonUpper95 < SessionSkipThresh && bucket.Trades >= SessionSkipMinSample)
            {
                // Cooldown 7d untuk skip
                var lastSkip = state.AuditHistory.FirstOrDefault(a =>
                    a.Action == "SessionSkip" && a.Bucket == bucket.Label);
                if (lastSkip != null && (DateTimeOffset.UtcNow - lastSkip.Timestamp) < SessionSkipCool)
                {
                    _log.LogInformation(
                        "Adaptive SessionSkip {Session}: would activate but cooldown 7d active",
                        bucket.Label);
                    continue;
                }

                var skipUntil = DateTimeOffset.UtcNow.Add(SessionSkipCool);
                string skipReason =
                    $"Session {bucket.Label}: WR {bucket.WinRate:P0} (Wilson 95% upper {bucket.WilsonUpper95:P0}) " +
                    $"< {SessionSkipThresh:P0}, n={bucket.Trades} — skip 7 hari (auto re-enable {skipUntil:yyyy-MM-dd})";

                var skipUpdate = new AdaptiveStateUpdate(
                    SessionSkipUntilSet: new Dictionary<string, DateTimeOffset> { [bucket.Label] = skipUntil });
                var skipAudit = new AdaptiveAuditEntry(
                    Timestamp:    DateTimeOffset.UtcNow,
                    Action:       "SessionSkip",
                    Bucket:       bucket.Label,
                    Parameter:    $"sessionSkipUntil[{bucket.Label}]",
                    FromValue:    "active",
                    ToValue:      skipUntil.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Reason:       skipReason,
                    SampleSize:   bucket.Trades,
                    WilsonLower:  bucket.WilsonLower95,
                    WilsonUpper:  bucket.WilsonUpper95,
                    ExpectancyR:  bucket.ExpectancyR,
                    SnapshotId:   "");

                var snapId = _adaptive.Apply(skipUpdate, skipAudit);
                _log.LogWarning(
                    "🤖 Adaptive ACTION fired: SessionSkip[{Session}] until {Until} — {Reason} (snapshot={Snap})",
                    bucket.Label, skipUntil, skipReason, snapId);
                continue;
            }

            // ── Penalty adjustment: moderately low WR ───────────────────────
            if (bucket.WilsonUpper95 < SessionPenaltyThresh)
            {
                decimal current = state.SessionPenalty.TryGetValue(bucket.Label, out var v) ? v : 0m;
                if (current >= SessionPenaltyMax) continue;  // already at cap

                decimal target = Math.Min(current + SessionPenaltyStep, SessionPenaltyMax);

                // Cooldown 24h per session penalty adjust
                var lastAdjust = state.AuditHistory.FirstOrDefault(a =>
                    a.Action == "SessionPenalty" && a.Bucket == bucket.Label);
                if (lastAdjust != null && (DateTimeOffset.UtcNow - lastAdjust.Timestamp) < ActionCooldownH)
                {
                    continue;
                }

                string reason =
                    $"Session {bucket.Label}: WR {bucket.WinRate:P0} (Wilson 95% upper {bucket.WilsonUpper95:P0}) " +
                    $"< {SessionPenaltyThresh:P0}, n={bucket.Trades} → confidence penalty +{SessionPenaltyStep:F2}";

                var update = new AdaptiveStateUpdate(
                    SessionPenaltySet: new Dictionary<string, decimal> { [bucket.Label] = target });
                var audit = new AdaptiveAuditEntry(
                    Timestamp:    DateTimeOffset.UtcNow,
                    Action:       "SessionPenalty",
                    Bucket:       bucket.Label,
                    Parameter:    $"sessionPenalty[{bucket.Label}]",
                    FromValue:    current.ToString("F2"),
                    ToValue:      target.ToString("F2"),
                    Reason:       reason,
                    SampleSize:   bucket.Trades,
                    WilsonLower:  bucket.WilsonLower95,
                    WilsonUpper:  bucket.WilsonUpper95,
                    ExpectancyR:  bucket.ExpectancyR,
                    SnapshotId:   "");

                var snapId = _adaptive.Apply(update, audit);
                _log.LogWarning(
                    "🤖 Adaptive ACTION fired: SessionPenalty[{Session}] {From}→{To} — {Reason} (snapshot={Snap})",
                    bucket.Label, current, target, reason, snapId);
            }
        }
    }
}
