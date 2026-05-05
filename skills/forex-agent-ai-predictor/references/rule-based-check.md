---
capability: rule-based-check
---

# Rule-Based Pattern Check

Periksa pola kondisi yang diketahui meningkatkan atau menurunkan probabilitas sukses setup forex.

## Pattern Boosters (+poin)

**Triple confluence:** Trend MA + RSI + S/R semuanya selaras → +15 poin
**Entry dekat support/resistance kuat:** Harga dalam 5 pip dari level yang telah ditest 3+ kali → +10 poin
**RSI extremes dengan divergensi:** RSI < 30 dengan bullish divergence (BUY) atau > 70 dengan bearish divergence (SELL) → +10 poin
**Breakout setelah konsolidasi ketat:** Range sebelumnya sempit, breakout baru terjadi ke arah sinyal → +5 poin

## Pattern Reducers (−poin)

**Entry di tengah range:** Harga > 10 pip dari S/R terdekat di kedua sisi → −10 poin
**Counter-trend entry:** Sinyal berlawanan dengan trend HTF (H1 bearish tapi sinyal M15 BUY) → −15 poin
**RSI di zona netral (45–55) tanpa momentum jelas:** Sulit membedakan reversal vs. continuation → −10 poin
**Setup terlalu "sempurna":** Semua indikator extreme selaras = bisa jadi trap (contoh: RSI 28 + dekat support + candle hammer = setup buku teks yang sering di-trap institusional) → −5 poin, tambah warning

## Output

Daftar pattern yang ditemukan, poin yang ditambah/dikurangi, dan skor akhir layer ini (0–30).
