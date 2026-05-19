# Live Test Checklist — Session 2026-05-19

Verify semua feature baru dari 17 commit hari ini berjalan correctly di production.
Jalankan setelah `dotnet run` + frontend dev server up + EA connected.

---

## 0. Pre-flight

- [ ] Backend up: `curl -s http://localhost:8080/api/system/status` returns 200
- [ ] Frontend up: `http://localhost:3000` loads tanpa console error
- [ ] EA connected: `curl http://localhost:8080/api/mifx/status` shows `connected: true`
- [ ] Candle data present: `curl http://localhost:8080/api/market/candles?pair=EURUSD&timeframe=M15` returns ≥ 50 bars

---

## 1. QA Fix (commit `f6bad5d`)

### P0-3 Risk Gate Restored
- [ ] Trigger analysis manual saat confidence < 60% (e.g. weekend / low volatility)
- [ ] Verify `riskValidation.decision === 'NO-GO'` di response
- [ ] Verify reason mengandung "below 60% minimum"

### P0-1 Forming Candle Fix
- [ ] Trigger analysis 2-3 kali dalam 1 menit (sebelum candle close)
- [ ] Verify confidence TIDAK flip dramatis (sebelumnya 67%→89%→67% dalam detik)
- [ ] Pattern detected harus stable sampai candle next bar close

### P0-2 Veto Bypass
- [ ] Cek signal warnings untuk pattern `"LIQUIDITY SWEEP DENIED:"` atau `"BREAKOUT DENIED:"`
- [ ] Kalau ada warning ini, artinya re-check veto bekerja — sweep/breakout promote di-block

---

## 2. Chart Overlay Visual (commits `13ac8ba` `4ead991` `fb4adb5` `9e70a16`)

Switch chart timeframe M15 → H1 → D1 dan verify per-TF:

### Dynamic S/R Trendlines
- [ ] Dashed red line (Dynamic Resistance) connect swing highs
- [ ] Dashed green line (Dynamic Support) connect swing lows
- [ ] Strength label badge "Strong/Good/Weak" di ujung kanan
- [ ] Markers dot di tiap pivot

### BOS / CHoCH Markers
- [ ] Triangle marker = BOS (continuation)
- [ ] Circle marker = CHoCH (reversal) — lebih bold
- [ ] Horizontal dashed line dari swing yang di-break ke break point
- [ ] Label badge "BOS Bullish" / "CHoCH Bearish"

### FVG Zones
- [ ] Unfilled FVG: semi-transparent fill + dashed edges + label "FVG 8p ↑/↓"
- [ ] Filled FVG: faded outline saja (history)
- [ ] Extend dari formedAt ke edge kanan

### Order Block Zones
- [ ] Solid border + diagonal hatching pattern (distinct dari FVG dashed)
- [ ] Label "OB ↑ 12p" / "OB ↓ 8p" di sebelah kiri zone
- [ ] Mitigated OB faded vs Unmitigated bold

### Static S/R (existing)
- [ ] Semi-transparent zone band ±5p
- [ ] Solid priceLine dengan axis label "⬆ S" / "⬇ R"

---

## 3. Adaptive Learning (commits `24c64e6` → `a1f35cb`)

### Trade Journal Enrich
- [ ] Open new position
- [ ] Cek `data/{mode}/position-status.json` — verify field baru ada:
  - sessionAtEntry / regimeAtEntry / patternName / zoneAtEntry / sweepDetected / confidenceAtEntry
- [ ] Position close → verify `exitReason` populated (SL_HIT / TP_HIT / BREAKEVEN / TRAILING_STOP / MANUAL)
- [ ] MFE/MAE pips bergerak per tick sync

### /adaptive Dashboard Page
- [ ] Buka `http://localhost:3000/adaptive`
- [ ] Global gate banner: CLOSED kalau totalTradeCount < 50, OPEN kalau ≥ 50
- [ ] Engine Control card: master toggle + 4 per-action toggle
- [ ] Active Overrides: empty awal, ter-update saat Adaptive fire (butuh data ≥50 + bucket ≥20)
- [ ] Bucket tables (7 categories) render dengan Wilson CI

### Adaptive Engine (butuh ≥50 trade total)
- [ ] Check backend log saat 6h cycle: `"Adaptive cycle: global gate CLOSED"` atau `"🤖 Adaptive ACTION fired:"`
- [ ] Kalau action fire, verify snapshot files di `data/{mode}/adaptive-snapshots/{timestamp}/`:
  - config_before.json
  - config_after.json
  - reason.json
- [ ] Audit timeline di `/adaptive` show entry dengan rollback button

### Manual Test Rollback
- [ ] Kalau ada entry di audit timeline, click "↶ Rollback"
- [ ] Confirm dialog → verify state restored + new "Revert" audit entry

---

## 4. News Calendar (commit `d25a343`)

- [ ] `curl http://localhost:8080/api/news/upcoming?hours=24&currency=USD,EUR` returns events
- [ ] Cek `fetchError: null` (Forex Factory feed up)
- [ ] Banner di dashboard:
  - Hidden kalau no high-impact within 4h
  - Amber banner kalau high-impact within 4h (>60 min away)
  - Red banner kalau high-impact within 60 min
- [ ] Countdown text "in X min" / "X min ago" update tiap 5 min

---

## 5. UX Polish

### Multi-TF MA Chip (commit `3296743`)
- [ ] Trigger analysis → chip muncul di header next to SessionChip
- [ ] Format "M15↑ H1↑ D1↑" dengan arrow direction
- [ ] Color: emerald all-bull, red all-bear, amber mixed
- [ ] Tooltip "all bullish" / "all bearish" / "mixed"

### Sound Notifications (commit `51fe788`)
- [ ] Click 🔔 toggle di header → switch ke 🔕
- [ ] localStorage `forexai.soundEnabled` persists across reload
- [ ] Trigger auto-approve fire → hear rising 2-tone beep
- [ ] Manual close winning trade → hear ascending major triad
- [ ] Manual close losing trade → hear descending square wave
- [ ] Wait for SL/TP hit autonomous close → hear sound + toast "🔔 Trade auto-closed"

### NO-GO Overlay Fix (commit `48379d9`)
- [ ] Wait for signal NO-GO (confidence < 60%)
- [ ] Verify chart TIDAK menampilkan SL/TP box (sebelumnya leak)
- [ ] Active position tetap render box (correct)

---

## 6. Regression Sanity Checks

- [ ] Trigger analysis on fresh M15 → no console error
- [ ] Switch chart TF → all 5 overlay re-render correctly
- [ ] Pan/zoom chart → overlays follow candles (not sticky to viewport)
- [ ] Open/close position manually → position card + chart update real-time
- [ ] Refresh page mid-session → state restored (localStorage)
- [ ] Halt + Resume cycle → no lingering state issue

---

## 7. Failure Modes to Watch For

**Auto-revert fire kalau adaptive ngaco**: kalau 10 trade post-adjustment hasilnya negative expectancy, backend log `"🔥 Adaptive REGRESSION DETECTED"` + `"🤖 Adaptive AUTO-REVERT executed"`. Audit timeline tampilkan "Revert" entry. Ini **expected behavior** kalau adjustment ternyata salah.

**News feed source down**: `fetchError` populated, banner tetap hidden (graceful). Tidak crash sistem.

**Forming candle pattern flip**: kalau masih ada confidence flip dramatis (>15% dalam <30 detik), forming candle bug fix mungkin tidak ke-apply di backend running. Restart backend untuk pickup commit.

**Sound tidak bunyi**: browser block autoplay sampai user interact dengan page. Click anywhere dulu sebelum trigger event yang generate sound.

---

## 8. Data Collection Timeline

| Milestone | Target | Trigger |
|-----------|--------|---------|
| Account current trade count | ~12 | Now |
| Adaptive observe-ready | ≥30 trade | ~2-3 minggu live |
| **Adaptive global gate OPEN** | ≥50 trade | ~4-6 minggu live |
| Tier 1 actions reach ≥20 bucket | ~80 trade total | ~2 bulan live |
| Tier 2 manual-approve consideration | ≥200 trade | ~3-4 bulan |

Pantau `/adaptive` route untuk track progress. Setelah global gate OPEN, watch backend log untuk first action fire.

---

## 9. When to Open New Session

Buka sesi baru untuk:
- Bug yang muncul saat live test (deskripsikan dengan repro steps)
- Performance regression detected (cek `/adaptive` audit timeline)
- Adaptive auto-revert fire (review snapshot reason.json)
- Feature request baru (Settings advanced, dll)

Sesi ini selesai. 🎯
