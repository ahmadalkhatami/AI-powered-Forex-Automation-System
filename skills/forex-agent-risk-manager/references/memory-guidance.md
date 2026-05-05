---
name: memory-guidance
description: Panduan apa yang perlu dicatat Reza di akhir setiap sesi
---

# Memory Guidance — Reza

Di akhir setiap sesi, perbarui sanctum dengan informasi berikut sebelum keluar.

## Session Log (`sessions/YYYY-MM-DD.md`)

Catat:
- Sinyal yang dievaluasi (pair, arah, confluence score)
- Keputusan yang diambil (GO/NO-GO/GO_WITH_CAUTION) dan alasan singkat
- Parameter trade yang disetujui (jika GO): entry, SL, TP, lot size
- Hasil trade jika sudah closed (profit/loss dalam pip dan nilai)
- Kondisi akun di akhir sesi (equity, drawdown saat ini)

## MEMORY.md (Update jika ada yang penting)

Perbarui jika ada:
- Perubahan equity signifikan
- Pola yang muncul (misalnya "tiga kali berturut-turut NO-GO karena confluence rendah")
- Keputusan kalibrasi ulang risk parameters
- Milestone (pertama kali mencapai profit target, atau pertama kali drawdown mendekati 5%)

## Equity Tracker

Selalu update di MEMORY.md:
```
Peak Equity    : [nilai tertinggi yang pernah dicapai]
Current Equity : [saldo saat ini]
Drawdown Kini  : [%]
Total Trades   : [jumlah GO yang dieksekusi]
Win Rate       : [jika sudah ada data]
```

## Yang TIDAK Perlu Dicatat

- Detail teknikal analisis (itu urusan Farida, sudah di signal-output.json)
- Confidence score detail (itu urusan Zara, sudah di sinyal tervalidasi)
- Instruksi workflow (tidak berubah lintas sesi)
