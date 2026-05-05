---
capability: mt5-bridge
---

# Persiapan MT5 Bridge (Next Phase)

Capability ini adalah placeholder untuk integrasi MT5 di fase berikutnya. Saat ini hanya mendokumentasikan apa yang perlu disiapkan.

## Yang Dibutuhkan untuk MT5 Integration

**Python library:** `MetaTrader5` (pip install MetaTrader5 — hanya tersedia di Windows; untuk Mac gunakan Wine atau remote execution ke Windows VM)

**Script Python yang perlu dibuat:**
- `scripts/mt5_connect.py` — koneksi ke terminal MT5
- `scripts/mt5_execute_order.py` — kirim market/limit order
- `scripts/mt5_get_positions.py` — baca posisi aktif
- `scripts/mt5_get_account.py` — baca informasi akun (equity, margin)

**Flow eksekusi yang direncanakan:**
1. Axis menerima `risk-decision.json` dengan keputusan GO
2. Axis memanggil `mt5_execute_order.py` dengan parameter dari decision JSON
3. Script mengirim order ke MT5 terminal
4. Konfirmasi eksekusi diterima dan dicatat di execution log

## Catatan Platform (Mac)

MT5 Python library tidak native di macOS. Opsi:
- **Opsi 1:** Jalankan MT5 di VM Windows, execution scripts di-remote via SSH
- **Opsi 2:** Gunakan OANDA REST API sebagai broker (lebih native untuk Python di Mac)
- **Opsi 3:** Gunakan broker yang support FIX protocol dengan Python library

## Status Saat Ini

```
MT5 Bridge: BELUM AKTIF
Mode aktif: SIMULATION ONLY
```

Ketika Ahmad siap untuk fase ini, jalankan `/bmad-create-story` untuk membuat implementation story yang detail untuk MT5 integration.
