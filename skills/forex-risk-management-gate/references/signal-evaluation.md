---
stage: 2
name: Evaluasi Kualitas Sinyal
---

# Stage 2: Evaluasi Kualitas Sinyal

Nilai apakah sinyal yang diterima cukup kuat untuk dipertimbangkan. Gate ini memfilter sinyal lemah sebelum kalkulasi dilakukan.

## Pemeriksaan Wajib

**Confluence score:** Baca dari `signal-output.json` atau input user.
- Score 3/3 → Sinyal kuat, lanjut
- Score 2/3 → Sinyal cukup, lanjut dengan catatan
- Score 1/3 atau 0/3 → **AUTO NO-GO** — market tidak cukup aligned

**Arah sinyal vs. struktur risiko:** Jika sinyal BUY tapi harga dekat resistance kuat dengan ruang gerak kecil, catat sebagai warning. Sinyal SELL dekat support kuat juga catat. Risk Manager boleh mendowngrade confidence meski analisis teknikal mendukung.

**Warnings dari analyst:** Baca field `warnings` dari signal JSON. Setiap warning yang disebutkan harus diakui — jika ada warning serius (misalnya "news besar dalam 30 menit"), tunda trade dan output NO-GO sementara.

**Timestamp sinyal:** Apakah sinyal masih relevan? Sinyal yang dibuat > 4 jam lalu untuk timeframe M15 perlu dikonfirmasi ulang. Flagging saja, bukan auto NO-GO.

## Output Stage Ini

```
[EVALUASI SINYAL]
Sinyal          : BUY / SELL / HOLD
Confluence      : [n]/3
Kualitas        : Kuat / Cukup / Lemah
Warnings Aktif  : [list atau "Tidak ada"]
Freshness       : OK / Perlu konfirmasi ([usia sinyal])
Keputusan Stage : LANJUT / NO-GO ([alasan])
```
