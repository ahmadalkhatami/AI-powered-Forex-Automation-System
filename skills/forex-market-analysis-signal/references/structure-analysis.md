---
stage: 4
name: Analisis Struktur Harga (Support & Resistance)
---

# Stage 4: Analisis Struktur Harga — Support & Resistance

Petakan level-level kunci di sekitar harga saat ini. Tujuan stage ini adalah menemukan *di mana* pasar akan bereaksi — ini yang menentukan validitas setup entry dan menginformasikan SL/TP yang realistis untuk Risk Manager.

## Yang Perlu Diidentifikasi

**Resistance terdekat:** Level harga di atas harga saat ini di mana penjual historis aktif. Prioritaskan level yang paling banyak "disentuh" (multi-touch = level lebih kuat).

**Support terdekat:** Level di bawah harga saat ini di mana pembeli historis aktif. Sama — multi-touch lebih kuat.

**Zona konsolidasi:** Apakah harga saat ini berada di dalam range konsolidasi? Jika ya, entry di tengah range berisiko — sinyal lebih valid di dekat tepi range.

**Posisi harga saat ini:**
- Dekat resistance = lebih sulit untuk BUY, lebih menarik untuk SELL
- Dekat support = lebih sulit untuk SELL, lebih menarik untuk BUY
- Di tengah = ruang untuk bergerak ke kedua arah

**Breakout atau rejection yang baru terjadi:** Apakah harga baru saja breakout dari level kunci (menambah kekuatan sinyal di arah breakout) atau rejection dari level kunci (menambah sinyal counter-trend)?

## Output Stage Ini

```
[ANALISIS STRUKTUR]
Resistance Terdekat : [level harga]  (kekuatan: Kuat/Sedang/Lemah)
Support Terdekat    : [level harga]  (kekuatan: Kuat/Sedang/Lemah)
Posisi Harga        : Dekat Resistance / Dekat Support / Di Tengah Range
Ruang Gerak BUY     : [jarak ke resistance berikutnya dalam pip]
Ruang Gerak SELL    : [jarak ke support berikutnya dalam pip]
Event Terbaru       : Breakout / Rejection / Tidak ada
Catatan S/R         : [observasi kunci]
```

Jika data historis tidak tersedia untuk menentukan S/R secara presisi, gunakan level psikologis (angka bulat) dan tandai sebagai estimasi.
