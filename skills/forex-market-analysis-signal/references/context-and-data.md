---
stage: 1
name: Konteks dan Data Input
---

# Stage 1: Konteks dan Data Input

Pastikan Anda memiliki semua yang dibutuhkan sebelum analisis dimulai. Tujuan stage ini adalah mengunci konteks sesi — pair, timeframe, sumber data, dan kondisi saat ini — sehingga stage berikutnya bekerja dari landasan yang sama.

## Yang Perlu Dikonfirmasi

**Pair dan timeframe:** Konfirmasi dari args atau tanya user. Default: EUR/USD, M15. Jika user menyebut timeframe kedua (misalnya H1 untuk higher timeframe context), catat keduanya.

**Sumber data:** OANDA API atau data MT5. Jika user memberikan data mentah (OHLCV, screenshot, atau ringkasan), terima semua format — ekstrak informasi yang relevan.

**Kondisi pasar saat ini:** Harga saat ini, high/low sesi terakhir, dan apakah ada news/event besar yang dijadwalkan. Jika user tidak tahu, lanjutkan tanpa — ini bukan blocker.

**Tujuan analisis:** Apakah ini untuk sinyal entry baru, atau validasi posisi yang sudah ada? Ini mempengaruhi cara sintesis di Stage 5.

## Output Stage Ini

Sebelum lanjut, ringkas apa yang telah dikonfirmasi dalam format ini (di terminal):

```
[KONTEKS TERKUNCI]
Pair       : EUR/USD
Timeframe  : M15 (+ H1 HTF)
Sumber Data: OANDA API
Harga Kini : [harga]
Tujuan     : Sinyal entry baru
```

Jika ada yang belum diketahui, tandai sebagai `[tidak tersedia]` — jangan memblokir progress.
