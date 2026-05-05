---
stage: 4
name: Keputusan dan Output
---

# Stage 4: Keputusan Final dan Output

Kompilasikan semua temuan menjadi keputusan GO/NO-GO yang terdokumentasi dengan lengkap. Setiap keputusan harus bisa diaudit — kita perlu tahu *mengapa* trade diambil atau ditolak, bukan hanya hasilnya.

## Logika Keputusan Final

**GO** jika semua kondisi terpenuhi:
- Kondisi akun CLEAR (Stage 1)
- Kualitas sinyal LANJUT dengan score ≥ 2 (Stage 2)
- R/R ≥ 1.5 (Stage 3)
- Tidak ada warning kritis yang belum diselesaikan

**NO-GO** jika salah satu dari:
- Drawdown ≥ 10%
- Posisi terbuka ≥ 3
- Confluence score < 2
- R/R < 1.0
- Warning kritis aktif (news, ambiguitas teknikal besar)

**GO dengan catatan** jika:
- Semua kondisi terpenuhi tapi ada warning minor (R/R 1.5–1.8, sinyal agak stale, dsb)
- User harus konfirmasi eksplisit sebelum eksekusi

## Format Output JSON

Tulis ke `{project-root}/_bmad-output/planning-artifacts/risk-decision.json`:

```json
{
  "timestamp": "[ISO 8601]",
  "decision": "GO | NO-GO | GO_WITH_CAUTION",
  "signal_ref": "signal-output.json",
  "account": {
    "equity": 10000,
    "drawdown_pct": 2.5,
    "open_positions": 1
  },
  "signal_quality": {
    "direction": "BUY",
    "confluence_score": 3,
    "warnings": []
  },
  "trade_parameters": {
    "pair": "EUR/USD",
    "direction": "BUY",
    "entry": 1.0882,
    "stop_loss": 1.0860,
    "take_profit": 1.0926,
    "lot_size": 0.45,
    "sl_pips": 22,
    "tp_pips": 44,
    "risk_reward": 2.0,
    "risk_amount": 99.0,
    "potential_profit": 198.0
  },
  "no_go_reasons": [],
  "caution_notes": [],
  "workflow": "forex-risk-management-gate",
  "next_step": "forex-agent-execution (simulasi) atau konfirmasi manual"
}
```

## Output Terminal

```
═══════════════════════════════════════════
RISK GATE DECISION: [PAIR]
═══════════════════════════════════════════
Keputusan      : ✅ GO / ❌ NO-GO / ⚠️  GO WITH CAUTION
Arah Trade     : BUY / SELL
Entry          : [harga]
Stop Loss      : [harga] ([n] pip)
Take Profit    : [harga] ([n] pip)
Lot Size       : [n] lot
Risk/Reward    : 1:[n]
Risk Amount    : [jumlah]
═══════════════════════════════════════════
Alasan NO-GO / Catatan: [jika ada]
Output: _bmad-output/planning-artifacts/risk-decision.json
Next Step: /forex-agent-execution
═══════════════════════════════════════════
```

Jika mode headless, pass `risk-decision.json` ke execution agent langsung. Jika mode interaktif, tunggu konfirmasi user sebelum eksekusi — bahkan untuk keputusan GO.
