---
stage: 2
name: Analisis Trend (Moving Average)
---

# Stage 2: Analisis Trend — Moving Average

Tentukan bias trend yang dominan menggunakan Moving Average sebagai lensa utama. Tujuan: masuk ke Stage 3 dengan satu keputusan yang jelas — apakah pasar sedang bullish, bearish, atau sideways — beserta evidensnya.

## Indikator yang Digunakan

- **MA Cepat** (misalnya MA 20 atau MA 50 — tergantung data yang tersedia)
- **MA Lambat** (misalnya MA 100 atau MA 200)
- **Kemiringan MA** sebagai konfirmasi arah

## Yang Perlu Dinilai

**Posisi harga terhadap MA:** Harga di atas MA lambat = bias bullish. Di bawah = bias bearish. Terjepit di antara MA = sideways/konsolidasi.

**Konfigurasi MA:** Golden cross (MA cepat memotong MA lambat ke atas) adalah sinyal bullish. Death cross adalah sinyal bearish. Tentukan mana yang lebih relevan saat ini.

**Kekuatan trend:** MA yang "miring" dengan sudut tajam menunjukkan trend kuat. MA yang datar menunjukkan pasar ranging. Ini penting untuk Stage 5 — sinyal di pasar ranging perlu confidence lebih tinggi.

**Konteks HTF:** Jika timeframe H1 tersedia, apakah bias MA di H1 selaras dengan M15? Konfluens meningkatkan kualitas sinyal.

## Output Stage Ini

```
[ANALISIS TREND]
Bias MA       : Bullish / Bearish / Sideways
Konfigurasi   : [deskripsi posisi MA saat ini]
Kekuatan      : Kuat / Sedang / Lemah
Konfluens HTF : Ya / Tidak / Tidak tersedia
Catatan       : [observasi tambahan jika ada]
```

Jika data MA tidak tersedia, buat penilaian trend berdasarkan struktur price action (higher highs/higher lows untuk bullish, sebaliknya untuk bearish) dan tandai bahwa MA tidak dikonfirmasi.
