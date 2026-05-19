using ForexAI.Application.UseCases.GetAdaptiveStats;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Enums;
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

    // Cooldown adaptation (per roadmap § 3.3)
    private const int     CooldownBaseline      = 30;     // minutes — sync dengan SystemStateService
    private const int     CooldownMin           = 15;
    private const int     CooldownMax           = 120;
    private const int     CooldownExtendStep    = 15;     // extend +15 saat post-LOSS streak
    private const int     CooldownShrinkStep    = -10;    // shrink -10 saat post-LOSS recovery good
    private const int     CooldownObsWindowMin  = 30;     // observation window — "next trade dalam 30 menit setelah LOSS"
    private const decimal CooldownExtendThresh  = 0.70m;  // follow-loss rate ≥ 70% → extend
    private const decimal CooldownShrinkThresh  = 0.45m;  // follow-loss rate ≤ 45% (WR ≥ 55%) → shrink
    private const int     CooldownMinSample     = 15;     // butuh ≥ 15 post-LOSS sequence
    private static readonly TimeSpan CooldownActionCool = TimeSpan.FromHours(48);

    // Pattern disable (per roadmap § 3.4)
    private const decimal PatternDisableUpper   = 0.35m;  // Wilson upper < 35% → disable
    private const int     PatternMinSample      = 20;
    private static readonly TimeSpan PatternDisableDuration = TimeSpan.FromDays(30);
    private static readonly TimeSpan PatternActionCool      = TimeSpan.FromDays(30);

    // Regression detector (per roadmap § 9 safeguard)
    private const int     RegressionMinSample   = 10;     // butuh ≥ 10 trade post-adjust
    private const decimal RegressionExpectancyR = 0m;     // expectancy R < 0 → revert
    private static readonly TimeSpan RegressionLookback     = TimeSpan.FromDays(7);   // only check audit ≤ 7d old
    private static readonly TimeSpan RegressionMinAge       = TimeSpan.FromHours(6);  // butuh ≥ 6h sejak adjust untuk fair sample

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

        // ── Performance regression detector — auto-revert latest adjustment
        //    kalau post-adjust expectancy negatif (artinya adjustment malah merugikan).
        //    Run BEFORE evaluate new actions supaya bad adjustment di-rollback dulu.
        using (var regScope = _services.CreateScope())
        {
            var repo = regScope.ServiceProvider.GetRequiredService<ITradePositionRepository>();
            await CheckRegressionAndRevertAsync(repo, _adaptive.Current);
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

        // ── Action 3: Cooldown length adaptation (per direction) ───────────
        if (!_adaptive.Current.CooldownActionDisabled)
        {
            using var scope2 = _services.CreateScope();
            var repo = scope2.ServiceProvider.GetRequiredService<ITradePositionRepository>();
            await EvaluateCooldownAsync(repo, _adaptive.Current);
        }
        else
            _log.LogInformation("Adaptive cycle: Cooldown action disabled — skip");

        // ── Action 4: Pattern enable/disable ───────────────────────────────
        if (!_adaptive.Current.PatternActionDisabled)
            EvaluatePatternDisable(stats, _adaptive.Current);
        else
            _log.LogInformation("Adaptive cycle: Pattern action disabled — skip");
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

    /// <summary>
    /// Action 3: cooldown length adaptation per direction (BUY/SELL track terpisah).
    /// Metric: dari semua LOSS trade arah X, berapa % yang next-same-direction trade
    /// dalam 30 menit juga LOSS? Kalau ≥ 70% → extend cooldown (+15 min);
    /// kalau ≤ 45% → shrink (-10 min). Bounds [15, 120], cooldown 48h per direction.
    /// </summary>
    private async Task EvaluateCooldownAsync(ITradePositionRepository repo, AdaptiveState state)
    {
        var all = await repo.GetAllAsync();
        var closed = all
            .Where(p => p.Status == TradeStatus.CLOSED_WIN || p.Status == TradeStatus.CLOSED_LOSS)
            .Where(p => p.OpenedAt.HasValue && p.ClosedAt.HasValue)
            .OrderBy(p => p.OpenedAt)
            .ToList();

        foreach (var direction in new[] { SignalDirection.BUY, SignalDirection.SELL })
        {
            var dirKey = direction.ToString();

            // Find post-LOSS sequence pairs:
            // (lossTrade, followTrade) where:
            //   - lossTrade.Status == LOSS && lossTrade.Direction == direction
            //   - followTrade.Direction == direction
            //   - followTrade.OpenedAt - lossTrade.ClosedAt < CooldownObsWindowMin
            int sequenceCount = 0;
            int followLossCount = 0;
            var sameDir = closed.Where(p => p.Direction == direction).ToList();
            for (int i = 0; i < sameDir.Count - 1; i++)
            {
                if (sameDir[i].Status != TradeStatus.CLOSED_LOSS) continue;
                var followTrade = sameDir[i + 1];
                var gapMin = (followTrade.OpenedAt!.Value - sameDir[i].ClosedAt!.Value).TotalMinutes;
                if (gapMin > CooldownObsWindowMin) continue;
                sequenceCount++;
                if (followTrade.Status == TradeStatus.CLOSED_LOSS) followLossCount++;
            }

            if (sequenceCount < CooldownMinSample)
            {
                _log.LogInformation(
                    "Adaptive Cooldown[{Dir}]: sequence n={N} < {Min}, skip",
                    dirKey, sequenceCount, CooldownMinSample);
                continue;
            }

            decimal followLossRate = (decimal)followLossCount / sequenceCount;
            int current = state.CooldownOverride.TryGetValue(dirKey, out var v) ? v : CooldownBaseline;

            int? target = null;
            string reason = "";
            if (followLossRate >= CooldownExtendThresh && current < CooldownMax)
            {
                target = Math.Min(current + CooldownExtendStep, CooldownMax);
                reason = $"Cooldown[{dirKey}]: post-LOSS follow-loss rate {followLossRate:P0} ≥ {CooldownExtendThresh:P0} " +
                         $"(n={sequenceCount}) → extend +{CooldownExtendStep} min";
            }
            else if (followLossRate <= CooldownShrinkThresh && current > CooldownMin)
            {
                target = Math.Max(current + CooldownShrinkStep, CooldownMin);
                reason = $"Cooldown[{dirKey}]: post-LOSS follow-loss rate {followLossRate:P0} ≤ {CooldownShrinkThresh:P0} " +
                         $"(n={sequenceCount}) → shrink {CooldownShrinkStep} min";
            }

            if (target is null) continue;

            // Cooldown 48h per direction
            var lastAdjust = state.AuditHistory.FirstOrDefault(a =>
                a.Action == "CooldownAdapt" && a.Bucket == dirKey);
            if (lastAdjust != null && (DateTimeOffset.UtcNow - lastAdjust.Timestamp) < CooldownActionCool)
            {
                _log.LogInformation("Adaptive Cooldown[{Dir}]: would adjust but cooldown 48h active", dirKey);
                continue;
            }

            var update = new AdaptiveStateUpdate(
                CooldownOverrideSet: new Dictionary<string, int> { [dirKey] = target.Value });
            var audit = new AdaptiveAuditEntry(
                Timestamp:    DateTimeOffset.UtcNow,
                Action:       "CooldownAdapt",
                Bucket:       dirKey,
                Parameter:    $"cooldownMinutes[{dirKey}]",
                FromValue:    current.ToString(),
                ToValue:      target.Value.ToString(),
                Reason:       reason,
                SampleSize:   sequenceCount,
                WilsonLower:  null,
                WilsonUpper:  null,
                ExpectancyR:  null,
                SnapshotId:   "");

            var snapId = _adaptive.Apply(update, audit);
            _log.LogWarning(
                "🤖 Adaptive ACTION fired: CooldownAdapt[{Dir}] {From}→{To} min — {Reason} (snapshot={Snap})",
                dirKey, current, target.Value, reason, snapId);
        }
    }

    /// <summary>
    /// Action 4: disable pattern boost kalau pattern ini consistently lose.
    /// Trigger: Wilson upper 95% &lt; 35% selama ≥ 20 trade. Disable 30 hari (auto re-enable).
    /// </summary>
    private void EvaluatePatternDisable(AdaptiveStatsResult stats, AdaptiveState state)
    {
        foreach (var bucket in stats.ByPattern)
        {
            if (bucket.Trades < PatternMinSample) continue;
            if (bucket.Label == "None") continue;  // "None" = no pattern, tidak ada boost untuk disable

            if (bucket.WilsonUpper95 >= PatternDisableUpper) continue;  // bucket masih acceptable

            // Sudah disabled?
            if (state.PatternDisableUntil.TryGetValue(bucket.Label, out var until) && until > DateTimeOffset.UtcNow)
            {
                continue;  // masih dalam disable period
            }

            // Cooldown 30d per pattern (sama dengan disable duration)
            var lastDisable = state.AuditHistory.FirstOrDefault(a =>
                a.Action == "PatternDisable" && a.Bucket == bucket.Label);
            if (lastDisable != null && (DateTimeOffset.UtcNow - lastDisable.Timestamp) < PatternActionCool)
            {
                continue;
            }

            var disableUntil = DateTimeOffset.UtcNow.Add(PatternDisableDuration);
            string reason =
                $"Pattern {bucket.Label}: WR {bucket.WinRate:P0} (Wilson 95% upper {bucket.WilsonUpper95:P0}) " +
                $"< {PatternDisableUpper:P0}, n={bucket.Trades} — disable boost 30 hari (auto re-enable {disableUntil:yyyy-MM-dd})";

            var update = new AdaptiveStateUpdate(
                PatternDisableUntilSet: new Dictionary<string, DateTimeOffset> { [bucket.Label] = disableUntil });
            var audit = new AdaptiveAuditEntry(
                Timestamp:    DateTimeOffset.UtcNow,
                Action:       "PatternDisable",
                Bucket:       bucket.Label,
                Parameter:    $"patternDisableUntil[{bucket.Label}]",
                FromValue:    "active",
                ToValue:      disableUntil.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Reason:       reason,
                SampleSize:   bucket.Trades,
                WilsonLower:  bucket.WilsonLower95,
                WilsonUpper:  bucket.WilsonUpper95,
                ExpectancyR:  bucket.ExpectancyR,
                SnapshotId:   "");

            var snapId = _adaptive.Apply(update, audit);
            _log.LogWarning(
                "🤖 Adaptive ACTION fired: PatternDisable[{Pattern}] until {Until} — {Reason} (snapshot={Snap})",
                bucket.Label, disableUntil, reason, snapId);
        }
    }

    /// <summary>
    /// Performance regression detector: kalau adjustment terbaru (≤ 7 hari, ≥ 6 jam)
    /// diikuti oleh ≥ 10 trade dengan expectancy R negatif, auto-revert via rollback
    /// ke snapshot before-state. Mencegah Adaptive Engine "chase market" ke arah yang salah.
    ///
    /// <para>Skip "Revert" entries (jangan revert revert). Hanya satu revert per cycle
    /// untuk avoid cascade.</para>
    /// </summary>
    private async Task CheckRegressionAndRevertAsync(ITradePositionRepository repo, AdaptiveState state)
    {
        // Find latest substantive audit entry (non-Revert)
        var latest = state.AuditHistory.FirstOrDefault(a => a.Action != "Revert");
        if (latest is null) return;

        var age = DateTimeOffset.UtcNow - latest.Timestamp;
        if (age < RegressionMinAge)
        {
            _log.LogInformation(
                "Adaptive regression check: latest adjust {Action}[{Bucket}] too recent ({Age:F1}h < {Min}h) — skip",
                latest.Action, latest.Bucket, age.TotalHours, RegressionMinAge.TotalHours);
            return;
        }
        if (age > RegressionLookback)
        {
            _log.LogInformation(
                "Adaptive regression check: latest adjust {Action}[{Bucket}] too old ({Age:F1}d > {Max}d) — skip",
                latest.Action, latest.Bucket, age.TotalDays, RegressionLookback.TotalDays);
            return;
        }

        var all = await repo.GetAllAsync();
        var postAdjust = all
            .Where(p => (p.Status == TradeStatus.CLOSED_WIN || p.Status == TradeStatus.CLOSED_LOSS)
                     && p.ClosedAt.HasValue
                     && p.ClosedAt.Value > latest.Timestamp)
            .ToList();

        if (postAdjust.Count < RegressionMinSample)
        {
            _log.LogInformation(
                "Adaptive regression check: only {N} post-adjust trades (need ≥ {Min}) — skip",
                postAdjust.Count, RegressionMinSample);
            return;
        }

        decimal totalPnl = postAdjust.Sum(p => p.FloatingPnl);
        decimal totalRisk = postAdjust.Where(p => p.RiskAmount > 0m).Sum(p => p.RiskAmount);
        decimal expectancyR = totalRisk > 0m ? totalPnl / totalRisk : 0m;

        if (expectancyR >= RegressionExpectancyR)
        {
            _log.LogInformation(
                "Adaptive regression check: post-adjust expectancy {ExpR:F2}R ≥ 0, no revert (n={N})",
                expectancyR, postAdjust.Count);
            return;
        }

        // Trigger auto-revert
        _log.LogWarning(
            "🔥 Adaptive REGRESSION DETECTED: {Action}[{Bucket}] caused expectancy {ExpR:F2}R over {N} trade — auto-revert to snapshot {Snap}",
            latest.Action, latest.Bucket, expectancyR, postAdjust.Count, latest.SnapshotId);

        bool ok = _adaptive.Rollback(latest.SnapshotId, "AutoRevert-RegressionDetector");
        if (ok)
        {
            _log.LogWarning(
                "🤖 Adaptive AUTO-REVERT executed: rolled back {Action}[{Bucket}] (expectancy {ExpR:F2}R over {N} trade)",
                latest.Action, latest.Bucket, expectancyR, postAdjust.Count);
        }
        else
        {
            _log.LogError(
                "Adaptive AUTO-REVERT FAILED: snapshot {Snap} could not be restored — manual intervention needed",
                latest.SnapshotId);
        }
    }
}
