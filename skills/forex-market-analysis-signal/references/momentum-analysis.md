---
stage: 3
name: Analisis Momentum (RSI)
---

# Stage 3: Analisis Momentum — RSI

Nilai momentum pasar menggunakan RSI. Tujuan stage ini bukan sekadar mengklasifikasikan RSI sebagai "overbought atau oversold" — tapi memahami apa yang RSI katakan *dalam konteks bias trend* dari Stage 2.

## Parameter RSI

- **Periode standar:** RSI 14
- **Zona overbought:** > 70
- **Zona oversold:** < 30
- **Zona netral:** 30–70 (dengan 50 sebagai garis tengah)

## Yang Perlu Dinilai

**Nilai RSI saat ini dan zona:** Klasifikasikan ke dalam overbought, netral-atas (50–70), netral-bawah (30–50), atau oversold.

**Kesesuaian dengan bias trend (Stage 2):**
- Trend bullish + RSI oversold atau netral-bawah yang sedang naik = setup ideal untuk BUY
- Trend bearish + RSI overbought atau netral-atas yang sedang turun = setup ideal untuk SELL
- Divergensi (trend bullish tapi RSI gagal membuat high baru) = sinyal melemah, catat sebagai warning

**Divergensi RSI:** Apakah ada divergensi bullish (harga lower low tapi RSI higher low) atau bearish (harga higher high tapi RSI lower high)? Ini adalah sinyal kualitas tinggi yang perlu dicatat eksplisit.

**Momentum saat ini:** RSI sedang naik atau turun? Arah momentum lebih penting dari nilai absolut.

## Output Stage Ini

```
[ANALISIS MOMENTUM]
RSI Saat Ini  : [nilai]
Zona          : Overbought / Netral-Atas / Netral-Bawah / Oversold
Arah Momentum : Naik / Turun / Sideways
Kesesuaian    : Selaras / Bertentangan / Netral dengan bias trend
Divergensi    : Ada (Bullish/Bearish) / Tidak ada
Catatan       : [observasi penting]
```
