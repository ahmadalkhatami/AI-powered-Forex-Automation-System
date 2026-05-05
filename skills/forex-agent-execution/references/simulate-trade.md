---
capability: simulate-trade
---

# Simulasi Eksekusi Trade

Terima `risk-decision.json` dengan keputusan GO dan simulasikan eksekusi trade. Dokumentasikan seolah trade benar-benar dieksekusi — ini membangun execution history yang valid untuk backtesting.

## Pre-Execution Checklist

Sebelum mensimulasikan eksekusi, verifikasi:
- [ ] Keputusan adalah GO (bukan NO-GO atau GO_WITH_CAUTION tanpa konfirmasi)
- [ ] Timestamp risk decision < 30 menit yang lalu (sinyal stale tidak dieksekusi)
- [ ] Parameter trade lengkap: pair, direction, entry, SL, TP, lot size

Jika ada yang gagal: tolak eksekusi dengan alasan yang jelas.

## Simulasi Eksekusi

Buat execution record dengan format berikut dan tulis ke `{project-root}/_bmad-output/implementation-artifacts/execution-log.json` (append, bukan overwrite):

```json
{
  "trade_id": "SIM-[YYYYMMDD]-[sequence]",
  "status": "SIMULATED_OPEN",
  "executed_at": "[ISO 8601]",
  "pair": "EUR/USD",
  "direction": "BUY",
  "entry_price": 1.0882,
  "stop_loss": 1.0860,
  "take_profit": 1.0926,
  "lot_size": 0.45,
  "sl_pips": 22,
  "tp_pips": 44,
  "risk_amount": 99.0,
  "potential_profit": 198.0,
  "risk_reward": 2.0,
  "source_signal": "signal-output.json",
  "source_decision": "risk-decision.json",
  "mode": "SIMULATION",
  "notes": ""
}
```

## Output Terminal

```
⚡ TRADE DISIMULASIKAN
═══════════════════════════════════════════
Trade ID  : SIM-[ID]
Pair      : EUR/USD BUY
Entry     : [harga]
SL        : [harga] ([pip] pip)
TP        : [harga] ([pip] pip)
Lot       : [n]
Mode      : SIMULATION
═══════════════════════════════════════════
Posisi aktif: [n] / 3
Log: _bmad-output/implementation-artifacts/execution-log.json
```

Setelah eksekusi dicatat, tambahkan posisi ini ke daftar posisi aktif di sanctum.
