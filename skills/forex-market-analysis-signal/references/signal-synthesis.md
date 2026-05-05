---
stage: 5
name: Sintesis Sinyal dan Output
---

# Stage 5: Sintesis Sinyal dan Output

Sintetiskan semua temuan dari Stage 2–4 menjadi satu sinyal trading terstruktur dengan confidence score dan kategori. Sistem harus jujur — HOLD adalah output valid dan wajib dikeluarkan jika kondisi tidak kuat.

## Anti-Overconfidence Principle

**Confidence tidak pernah boleh mencapai 1.00 dalam kondisi apapun.**

Forex market adalah sistem probabilistik, bukan deterministik. Bahkan setup yang paling sempurna secara teknikal bisa gagal karena:
- Institutional stop hunt di level support/resistance yang terlihat jelas
- News event yang tidak terprediksi
- Likuiditas rendah yang mengubah perilaku harga
- Perubahan sentimen makro yang tiba-tiba

**Maximum confidence yang diizinkan sistem: 0.85**

Ini bukan kelemahan sistem — ini adalah kejujuran sistem. Confidence 0.85 artinya: "Berdasarkan semua yang terlihat, setup ini sangat kuat, tapi kami tidak pernah 100% yakin dalam trading."

---

## Scoring System

### Skala Skor Komponen

Gunakan skala ini secara konsisten. Nilai 1.0 tidak pernah digunakan:

| Nilai | Interpretasi |
|-------|-------------|
| **0.9** | Sangat kuat — kondisi terbaik yang mungkin untuk faktor ini |
| **0.8** | Kuat — kondisi di atas rata-rata, mendukung kuat |
| **0.7** | Cukup — kondisi solid tapi tidak ideal |
| **0.5** | Sedang — ada, tapi dengan kelemahan signifikan |
| **0.3** | Lemah — ada tapi hampir tidak berkontribusi |
| **0.1** | Sangat lemah / netral |
| **0.0** | Berlawanan dengan arah sinyal |

### Komponen Skor (masing-masing 0.0–0.9)

**Trend Score (bobot 35%)**

| Kondisi | Skor |
|---------|------|
| Bullish kuat — MA steep, gap MA lebar, HTF aligned, momentum jelas | **0.9** |
| Bullish kuat — tapi HTF belum dikonfirmasi atau golden cross sudah lama | **0.8** |
| Bullish sedang — MA miring moderat, gap MA sempit | **0.5** |
| Sideways / ranging | 0.1 |
| Bearish | 0.0 |

> **Rule:** Trend "Sedang" tidak pernah menghasilkan "Strong Buy" sendirian.

**Momentum Score (bobot 35%)**

| Kondisi RSI | Skor |
|-------------|------|
| Oversold (<30) + RSI naik + divergensi bullish kuat | **0.9** |
| Oversold (<30) + RSI naik (tanpa divergensi) | **0.8** |
| Netral-Bawah (30–45) + divergensi bullish | 0.6 |
| Netral-Bawah (30–45) + RSI naik tanpa divergensi | 0.35 |
| Netral (45–55) + arah tidak jelas | 0.2 |
| Netral-Atas (55–70) | 0.1 |
| Overbought (>70) | 0.0 |

> **Rule:** RSI tidak di zona oversold tidak pernah mendapat skor di atas 0.6.

**Structure Score (bobot 30%)**

| Kondisi | Skor |
|---------|------|
| Support kuat multi-layer + candle confirmation kuat (engulfing besar) | **0.9** |
| Support kuat + candle confirmation (pin bar / engulfing standar) | **0.8** |
| Support kuat + TANPA konfirmasi candle | 0.5 |
| Support sedang + TANPA konfirmasi | 0.3 |
| Di tengah range | 0.1 |
| Dekat resistance (untuk setup BUY) | 0.0 |

### Formula Confidence Score

```
base_score = (trend_score × 0.35) + (momentum_score × 0.35) + (structure_score × 0.30)
```

**Candle Confirmation Gate:**
- Jika candle confirmation **ADA**: `final_score = base_score`
- Jika candle confirmation **TIDAK ADA**: `final_score = base_score × 0.7`

**Confidence Cap (WAJIB):**
```
confidence_score = min(final_score, 0.85)
```

Ini selalu diterapkan, tanpa pengecualian.

## Hard Gates — Wajib HOLD Jika Salah Satu Terpenuhi

Cek kondisi ini sebelum klasifikasi. Jika terpenuhi, langsung output HOLD tanpa melihat skor:

1. **Tidak ada candle confirmation** DAN `base_score < 0.65` → HOLD
2. **Trend sideways** (bukan bullish/bearish) → HOLD
3. **RSI > 65** untuk setup BUY → HOLD (momentum jual akan segera datang)
4. **Harga di tengah range** (lebih dari 50% jarak S/R dari kedua sisi) → HOLD

## Klasifikasi Sinyal

| Confidence Score | Signal | Kategori | Implikasi |
|-----------------|--------|----------|-----------|
| 0.80 – 0.85 | BUY | Strong Buy | Setup terkuat — full lot, SL normal |
| 0.70 – 0.79 | BUY | Buy | Setup solid — full lot, waspadai warning |
| 0.60 – 0.69 | BUY | Weak Buy | Setup cukup — lot dikurangi, SL lebih ketat |
| < 0.60 | HOLD | HOLD | Setup tidak layak entry saat ini |

> **Catatan:** Maximum confidence 0.85 berarti "Strong Buy" adalah ceiling sistem. Setiap skor di atas 0.85 hasil kalkulasi WAJIB di-cap ke 0.85 — bukan karena sistemnya salah, tapi karena pasar selalu memiliki ketidakpastian yang tidak bisa di-eliminate.

## Format Output JSON

Tulis output berikut ke `{project-root}/_bmad-output/planning-artifacts/signal-output.json`:

```json
{
  "timestamp": "[ISO 8601 timestamp]",
  "pair": "EUR/USD",
  "timeframe": "M15",
  "signal": "BUY | SELL | HOLD",
  "signal_category": "Strong Buy | Weak Buy | HOLD | Weak Sell | Strong Sell",
  "confluence_score": 3,
  "confidence_score": 0.53,
  "score_breakdown": {
    "trend_score": 0.5,
    "trend_weight": 0.35,
    "momentum_score": 0.6,
    "momentum_weight": 0.35,
    "structure_score": 0.5,
    "structure_weight": 0.30,
    "candle_confirmed": false,
    "confirmation_multiplier": 0.7,
    "base_score": 0.535,
    "final_score": 0.375
  },
  "hard_gate_triggered": "NO_CONFIRMATION + base_score < 0.65",
  "analysis": {
    "trend": {
      "bias": "Bullish | Bearish | Sideways",
      "strength": "Kuat | Sedang | Lemah",
      "htf_aligned": true
    },
    "momentum": {
      "rsi_value": 45.2,
      "zone": "Netral-Bawah",
      "direction": "Naik",
      "divergence": null
    },
    "structure": {
      "nearest_resistance": 1.0920,
      "nearest_support": 1.0880,
      "price_position": "Dekat Support",
      "room_to_target_pips": 40,
      "candle_confirmation": false
    }
  },
  "reasoning": "[Narasi singkat 2-3 kalimat mengapa sinyal ini dihasilkan]",
  "warnings": ["[Setiap faktor risiko atau ambiguitas yang perlu diketahui Risk Manager]"],
  "recommended_entry_zone": "[harga atau range, atau null jika HOLD]",
  "wait_for": "[Kondisi spesifik yang harus terpenuhi sebelum setup ini bisa di-re-evaluate]",
  "workflow": "forex-market-analysis-signal",
  "next_step": "forex-risk-management-gate"
}
```

## Output Terminal

Setelah file ditulis, tampilkan ringkasan di terminal:

```
═══════════════════════════════════════════
SINYAL FOREX: [PAIR] [TIMEFRAME]
═══════════════════════════════════════════
Sinyal         : BUY (Strong Buy) / HOLD
Confidence     : 0.53 / 1.00
Skor Konfluens : [n]/3
Gate Triggered : [hard gate yang aktif, atau "-"]
Timestamp      : [waktu]
Output         : _bmad-output/planning-artifacts/signal-output.json
Next Step      : Jalankan /forex-risk-management-gate
═══════════════════════════════════════════
```

Jika mode headless, langsung pass output JSON ke workflow berikutnya tanpa menunggu konfirmasi.
