---
stage: 1
name: Validasi Input dan Kondisi Akun
---

# Stage 1: Validasi Input dan Kondisi Akun

Sebelum mengevaluasi sinyal, pastikan kondisi akun memungkinkan trade baru. Ini adalah pre-flight check — jika akun sudah dalam kondisi terbatas, tidak ada sinyal sehebat apapun yang boleh dieksekusi.

## Data yang Dibutuhkan

**Saldo akun saat ini (equity):** Tanya user atau load dari config. Ini adalah basis kalkulasi 1% risk.

**Drawdown saat ini:** `(Peak equity - Current equity) / Peak equity * 100`. Jika user tidak tahu peak equity, gunakan saldo awal sebagai proxy. Jika drawdown ≥ 10% → **AUTO NO-GO**, tulis alasan, skip ke Stage 4.

**Jumlah posisi terbuka saat ini:** Berapa trade yang sedang aktif? Jika ≥ 3 → **AUTO NO-GO**, tulis alasan, skip ke Stage 4. Jika posisi ada yang sedang profit besar, catat — ini relevan untuk sizing.

**Pair yang akan ditrade:** Apakah sudah ada posisi terbuka di EUR/USD? Jika ya, apakah menambah posisi di arah yang sama (pyramiding) atau arah berlawanan (hedging)? Keduanya butuh konfirmasi eksplisit dari user.

## Output Stage Ini

```
[KONDISI AKUN]
Saldo Equity    : [jumlah] [currency]
Drawdown Kini   : [%] (Limit: 10%)
Posisi Terbuka  : [n] / 3
Status          : CLEAR / NO-GO (Drawdown) / NO-GO (Max Posisi)
Catatan         : [apapun yang relevan]
```

Jika status NO-GO, langsung pindah ke Stage 4 dengan alasan yang jelas.
