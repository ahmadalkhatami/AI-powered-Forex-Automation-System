---
stage: 3
name: Kalkulasi Parameter Trade
---

# Stage 3: Kalkulasi Parameter Trade

Hitung semua parameter trade secara presisi. Ini adalah kalkulasi matematika — tidak ada judgment, hanya angka yang benar.

## Formula Kalkulasi

### Lot Size (Position Sizing)

```
Risk Amount    = Equity × 1%
                 = [equity] × 0.01

Pip Value (EUR/USD, lot standar) = $10 per pip per lot standar
Lot Size = Risk Amount / (SL dalam pip × Pip Value)
```

**Contoh:** Equity $10,000 → Risk $100. SL = 20 pip. Pip value = $10.
Lot = $100 / (20 × $10) = $100 / $200 = 0.50 lot

Bulatkan lot ke 0.01 terdekat (precision MT5). Jika hasilnya < 0.01, ini adalah warning — trade mungkin terlalu kecil untuk dieksekusi dengan platform.

### Stop Loss (SL)

SL harus diletakkan di level yang *logis secara teknikal* — bukan hanya jarak arbitrer. Gunakan data dari Stage 4 analisis sinyal:

- Untuk BUY: SL di bawah support terdekat + buffer 2-5 pip (untuk menghindari stop hunt)
- Untuk SELL: SL di atas resistance terdekat + buffer 2-5 pip

Hitung jarak SL dalam pip dari entry yang direncanakan.

### Take Profit (TP)

TP ditempatkan sebelum level S/R berikutnya yang berlawanan dengan arah trade:

- Untuk BUY: TP sebelum resistance terdekat (beri ruang 3-5 pip dari level)
- Untuk SELL: TP sebelum support terdekat

**Risk/Reward Ratio:** `TP pip / SL pip`
- R/R ≥ 2.0 → Excellent
- R/R 1.5–2.0 → Acceptable
- R/R 1.0–1.5 → Warning (flagging tapi tidak auto NO-GO)
- R/R < 1.0 → NO-GO

### Harga Entry

Untuk mode simulasi: gunakan harga pasar saat ini (market order).
Untuk mode limit order: gunakan zona entry yang direkomendasikan analyst dari sinyal.

## Output Stage Ini

```
[KALKULASI TRADE]
Equity           : [jumlah]
Risk Amount      : [jumlah] (1%)
Entry            : [harga]
Stop Loss        : [harga] ([n] pip dari entry)
Take Profit      : [harga] ([n] pip dari entry)
Risk/Reward      : 1:[R/R ratio]
Lot Size         : [n] lot
Estimasi Profit  : [jumlah jika TP tercapai]
Estimasi Loss    : [jumlah jika SL tercapai]
Status R/R       : Excellent / Acceptable / Warning / NO-GO
```
