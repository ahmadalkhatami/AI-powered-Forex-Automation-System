---
capability: ml-output-interpretation
---

# Interpretasi Output Model ML

Untuk fase berikutnya ketika model Scikit-learn atau PyTorch sudah diintegrasikan, capability ini menginterpretasikan output model dan mengintegrasikannya ke confidence score.

## Format Output ML yang Diharapkan

Model klasifikasi BUY/SELL/HOLD diharapkan menghasilkan:

```json
{
  "model_prediction": "BUY",
  "probabilities": {
    "BUY": 0.68,
    "SELL": 0.15,
    "HOLD": 0.17
  },
  "model_version": "v0.1-simple-classifier",
  "features_used": ["ma_cross", "rsi_14", "sr_distance_pip"]
}
```

## Cara Mengintegrasikan ke Confidence Score

**Model agrees with signal:** Probabilitas prediksi model untuk arah sinyal ≥ 0.65 → tambah 20 poin ke confidence
**Model neutral:** Probabilitas 0.45–0.65 → tambah 10 poin
**Model disagrees:** Probabilitas < 0.45 untuk arah sinyal → kurangi 20 poin dan tambah warning kritis

## Catatan Fase Awal

Sebelum model ML tersedia, capability ini mengeluarkan pesan:

```
[ML Predictor] Model belum tersedia. Confidence score dihitung dari rule-based 
dan LLM reasoning saja. Integrasikan model Python ke pipeline untuk mengaktifkan 
layer ini.
```

Ini tidak memblokir validasi — hanya mendokumentasikan bahwa satu layer belum aktif.
