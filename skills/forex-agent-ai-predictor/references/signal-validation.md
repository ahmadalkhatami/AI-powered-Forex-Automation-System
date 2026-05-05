---
capability: signal-validation
---

# Validasi Sinyal Lengkap + Confidence Score

Proses sinyal yang diterima melalui tiga lapis validasi dan hasilkan confidence score terkomposisi.

## Layer 1: Konsistensi Internal Sinyal

Periksa apakah semua komponen sinyal dari Farida saling konsisten:
- Apakah bias trend, RSI, dan struktur benar-benar selaras seperti yang diklaim?
- Apakah confluence_score yang dilaporkan akurat?
- Apakah ada kontradiksi yang diabaikan (misalnya "bullish" tapi RSI overbought tanpa penjelasan)?

Skor layer ini: 0–30 poin.

## Layer 2: Konteks Pasar Saat Ini

Load `references/market-context.md` untuk menilai faktor eksternal:
- Sesi trading saat ini (Asia/London/New York) dan implikasinya untuk EUR/USD
- Apakah ada event ekonomi besar dalam 24 jam ke depan?
- Apakah spread/volatilitas saat ini normal atau abnormal untuk pair ini?

Skor layer ini: 0–40 poin.

## Layer 3: Pattern Recognition (Rule-Based)

Load `references/rule-based-check.md` untuk cek pola yang diketahui meningkatkan atau menurunkan probabilitas sukses.

Skor layer ini: 0–30 poin.

## Confidence Score Final

```
Confidence = Layer1 + Layer2 + Layer3  (0–100)

≥ 75  → HIGH CONFIDENCE — rekomendasikan lanjut ke Risk Gate
60–74 → MEDIUM CONFIDENCE — lanjut dengan catatan
< 60  → LOW CONFIDENCE — rekomendasikan HOLD
```

## Output Format JSON

Tambahkan atau update sinyal dengan field berikut:

```json
{
  "predictor_validation": {
    "confidence_score": 72,
    "confidence_level": "MEDIUM",
    "recommendation": "PROCEED_WITH_CAUTION",
    "score_breakdown": {
      "internal_consistency": 25,
      "market_context": 28,
      "pattern_recognition": 19
    },
    "findings": [
      "Trend dan momentum selaras kuat (+)",
      "Sesi London baru buka, volatilitas meningkat (+)",
      "RSI mendekati zone netral, momentum belum konfirmasi penuh (-)"
    ],
    "override_signal": null,
    "predictor": "Zara",
    "validated_at": "[timestamp]"
  }
}
```

Jika confidence < 60, set `override_signal: "HOLD"` — ini akan direspek oleh Risk Manager sebagai auto NO-GO.
