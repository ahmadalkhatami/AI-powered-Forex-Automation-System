---
name: forex-market-analysis-signal
description: Analisis pasar forex multi-indikator dan generate sinyal BUY/SELL/HOLD. Use when user ingin analisis market, generate sinyal trading, atau memulai pipeline analisis forex.
---

# Forex Market Analysis Signal Workflow

## Overview

Workflow ini mengeksekusi analisis pasar forex terstruktur untuk EUR/USD (dan pair lain) pada timeframe M15 dan H1. Ia mengorkestrasi tiga lensa analisis — trend, momentum, dan struktur harga — untuk menghasilkan sinyal trading yang terukur dengan konteks yang cukup bagi Risk Manager untuk membuat keputusan eksekusi.

**Output:** Sinyal JSON terstruktur (`signal_output`) + log terminal, siap dikonsumsi oleh workflow `forex-risk-management-gate`.

**Args:** Opsional — `--pair EUR/USD` (default), `--timeframe M15` (default), `--headless` untuk eksekusi pipeline tanpa interaksi.

## Design Rationale

Workflow ini memisahkan *analisis* dari *keputusan risiko* secara sengaja. Market Analyst tidak tahu ukuran akun atau risk profile — ia hanya menilai kondisi pasar. Pemisahan ini membuat setiap komponen dapat di-test dan diganti secara independen.

## On Activation

Load config dari `{project-root}/_bmad/config.yaml` dan `{project-root}/_bmad/config.user.yaml` jika tersedia.

Baca pair dan timeframe dari args, atau gunakan default (EUR/USD, M15). Jika mode headless, jalankan semua stage tanpa interaksi dan tulis output ke file.

Sambut user dan tunjukkan stage yang akan dijalankan.

## Stage 1: Konteks dan Data Input

Load `references/context-and-data.md`.

Kondisi untuk lanjut ke Stage 2: pair, timeframe, dan sumber data telah dikonfirmasi; data harga mentah atau ringkasan OHLCV tersedia.

## Stage 2: Analisis Trend (Moving Average)

Load `references/trend-analysis.md`.

Kondisi untuk lanjut ke Stage 3: bias trend (bullish/bearish/sideways) telah ditentukan dengan evidens MA yang jelas.

## Stage 3: Analisis Momentum (RSI)

Load `references/momentum-analysis.md`.

Kondisi untuk lanjut ke Stage 4: kondisi RSI (overbought/oversold/netral) telah dinilai dan dikaitkan dengan bias trend.

## Stage 4: Analisis Struktur Harga (S/R)

Load `references/structure-analysis.md`.

Kondisi untuk lanjut ke Stage 5: level support/resistance kunci telah diidentifikasi; posisi harga saat ini relatif terhadap struktur telah dinilai.

## Stage 5: Sintesis Sinyal dan Output

Load `references/signal-synthesis.md`.

Kondisi selesai: sinyal JSON telah ditulis ke `{project-root}/_bmad-output/planning-artifacts/signal-output.json` dan diringkas di terminal.
