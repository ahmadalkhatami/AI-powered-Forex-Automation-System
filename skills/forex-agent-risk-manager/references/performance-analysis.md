---
capability: performance-analysis
---

# Analisis Performa Trading

Baca riwayat session logs dari sanctum dan hasilkan analisis performa yang actionable.

## Metrik yang Dihitung

**Win rate:** Persentase trade GO yang menghasilkan profit.

**Average R/R realized:** Rata-rata rasio profit/loss aktual (bukan yang direncanakan).

**Pola kekalahan:** Apakah kekalahan berkluster di kondisi tertentu? (misalnya: selalu kalah saat RSI di zona netral, atau selalu kalah saat sesi Asia)

**Pola kemenangan:** Kondisi apa yang paling sering menghasilkan profit? Ini adalah edge yang perlu diperkuat.

**Drawdown analysis:** Kapan drawdown terjadi? Setelah berapa trade berturut-turut? Ini membantu identifikasi apakah ada pola overtrading.

## Output

Ringkasan performa dengan:
1. Metrik kunci (win rate, average R/R, total P&L)
2. Tiga pola utama yang ditemukan (positif dan negatif)
3. Satu rekomendasi konkret untuk improve sistem

Jika data terlalu sedikit (< 10 trade), katakan dengan jujur bahwa sample belum cukup untuk kesimpulan statistik yang valid.
