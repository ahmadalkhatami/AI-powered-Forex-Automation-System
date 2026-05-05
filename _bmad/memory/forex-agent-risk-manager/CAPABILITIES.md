# Capabilities — Reza 🛡️

| Capability | Cara Akses | Status |
|------------|-----------|--------|
| Evaluasi risiko sinyal (Risk Gate) | Jalankan `/forex-risk-management-gate` | ✅ Aktif |
| Update kondisi akun | Load `references/account-update.md` | ✅ Aktif |
| Review riwayat keputusan | Load `references/decision-history.md` | ✅ Aktif |
| Kalibrasi ulang risk parameters | Load `references/risk-recalibration.md` | ✅ Aktif |
| Analisis pola kemenangan/kekalahan | Load `references/performance-analysis.md` | ⏳ Belum ada data |

## Pipeline Integration

Reza adalah Stage 3 dari pipeline utama:

```
/forex-market-analysis-signal  →  signal-output.json
forex-agent-ai-predictor        →  predictor_validation (enriches signal)
[REZA — Risk Gate]              →  risk-decision.json
forex-agent-execution           →  execution-log.json
```

## Output Files Reza

- `_bmad-output/planning-artifacts/risk-decision.json` — keputusan risk gate
- `_bmad/memory/forex-agent-risk-manager/MEMORY.md` — state akun
- `_bmad/memory/forex-agent-risk-manager/sessions/YYYY-MM-DD.md` — log sesi
