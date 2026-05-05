---
name: forex-risk-management-gate
description: Evaluasi risiko sinyal forex dan hitung parameter trade (lot, SL, TP). Use when user ingin evaluasi risiko sinyal, hitung lot size, atau memvalidasi apakah trade layak dieksekusi.
---

# Forex Risk Management Gate Workflow

## Overview

Workflow ini adalah gerbang keputusan antara sinyal analisis dan eksekusi trade. Ia menerima output dari `forex-market-analysis-signal`, memvalidasi kondisi risiko portofolio, menghitung parameter trade yang tepat, dan mengeluarkan keputusan GO / NO-GO yang terdokumentasi.

**Tidak ada trade yang dieksekusi tanpa melewati gate ini.** Ini adalah invariant sistem — bukan saran.

**Input:** `signal-output.json` dari workflow analisis, atau data sinyal manual dari user.
**Output:** `risk-decision.json` berisi keputusan GO/NO-GO + parameter trade lengkap.

**Args:** Opsional — `--signal <path>` untuk load sinyal dari file, `--account-balance <jumlah>` override saldo, `--headless` untuk pipeline otomatis.

## Design Rationale

Gate ini sengaja dipisah dari analisis sinyal karena dua alasan: (1) Risk profile berubah seiring waktu — drawdown yang sudah terjadi mempengaruhi apakah kita boleh masuk; (2) Kalkulasi lot, SL, TP adalah fungsi matematika yang harus konsisten, bukan judgment call yang bervariasi setiap sesi.

## Batas Risiko Sistem (Hard Limits — Tidak Dapat Dioverride)

- **Risk per trade:** 1% dari saldo akun
- **Max drawdown:** 10% — jika tercapai, sistem STOP otomatis, tidak ada trade baru
- **Max posisi terbuka:** 3 trade simultan
- **Minimum confluence score:** 2/3 — sinyal dengan skor 1 atau 0 otomatis NO-GO

## On Activation

Load config dari `{project-root}/_bmad/config.yaml` dan `{project-root}/_bmad/config.user.yaml` jika tersedia.

Coba load sinyal dari `{project-root}/_bmad-output/planning-artifacts/signal-output.json`. Jika tidak ada, minta user memberikan data sinyal secara manual.

Sambut user dan konfirmasi sinyal yang akan dievaluasi.

## Stage 1: Validasi Input dan Kondisi Akun

Load `references/account-validation.md`.

Kondisi untuk lanjut ke Stage 2: saldo akun diketahui, drawdown saat ini dihitung, jumlah posisi terbuka diketahui. Jika max drawdown atau max posisi sudah tercapai, keluarkan NO-GO langsung dan skip ke Stage 4.

## Stage 2: Evaluasi Kualitas Sinyal

Load `references/signal-evaluation.md`.

Kondisi untuk lanjut ke Stage 3: sinyal telah dievaluasi terhadap hard limits. Jika confluence score < 2, keluarkan NO-GO dengan alasan dan skip ke Stage 4.

## Stage 3: Kalkulasi Parameter Trade

Load `references/trade-calculation.md`.

Kondisi untuk lanjut ke Stage 4: lot size, SL (pips), TP (pips), dan risk/reward ratio telah dihitung. Jika R/R < 1.5, tandai sebagai warning tapi tidak otomatis NO-GO — biarkan user memutuskan.

## Stage 4: Keputusan dan Output

Load `references/decision-output.md`.

Kondisi selesai: `risk-decision.json` telah ditulis dan ringkasan ditampilkan di terminal. Jika GO, instruksi next step ke `forex-agent-execution` diberikan.
