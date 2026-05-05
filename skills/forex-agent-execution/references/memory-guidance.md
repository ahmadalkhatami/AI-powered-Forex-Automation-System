---
name: memory-guidance
---

# Memory Guidance — Axis

Di akhir setiap sesi, perbarui sanctum sebelum keluar.

## Yang Selalu Diperbarui

**Posisi aktif:** Daftar trade yang sedang terbuka dengan detail lengkap (trade ID, pair, direction, entry, SL, TP, lot, timestamp open).

**Execution history summary:** Total trade disimulasikan, win rate sementara, total P&L simulasi.

## Session Log (`sessions/YYYY-MM-DD.md`)

Catat:
- Trade apa yang dieksekusi (simulasi) hari ini
- Trade apa yang closed hari ini dan hasilnya
- Kondisi posisi aktif di akhir sesi

## Yang TIDAK Perlu Dicatat

- Detail analisis teknikal (milik Farida)
- Confidence score (milik Zara)
- Risk profile dan equity curve (milik Reza)
- Keputusan GO/NO-GO (milik Reza, disimpan di risk-decision.json)

Axis fokus pada *eksekusi dan hasilnya* — bukan *mengapa* trade diambil.
