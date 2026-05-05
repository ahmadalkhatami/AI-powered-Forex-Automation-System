---
capability: trade-log
---

# Log Hasil Trade (Closed Position)

Catat hasil trade yang sudah ditutup dan update semua komponen sistem yang relevan.

## Yang Dibutuhkan dari User

- Trade ID yang ditutup
- Harga close aktual
- Alasan close: SL hit, TP hit, atau manual close

## Yang Dilakukan

**Hitung hasil aktual:**
- Pip P&L: `(close price - entry price) × 10000` untuk EUR/USD BUY (negatif untuk SELL)
- Nilai P&L: `pip P&L × pip value × lot size`
- Bandingkan dengan rencana: apakah SL/TP tercapai sesuai yang direncanakan?

**Update execution log:** Ubah status trade dari `SIMULATED_OPEN` ke `SIMULATED_CLOSED` di `execution-log.json`. Tambahkan field:

```json
{
  "closed_at": "[ISO 8601]",
  "close_price": 1.0920,
  "close_reason": "TP_HIT | SL_HIT | MANUAL",
  "pnl_pips": 38,
  "pnl_value": 171.0,
  "status": "SIMULATED_CLOSED"
}
```

**Kirim update ke Reza:** Informasikan bahwa posisi sudah closed dengan P&L, sehingga Reza bisa update equity tracker.

**Update sanctum Axis:** Hapus dari daftar posisi aktif, tambah ke execution history.

## Output Terminal

```
✅ POSISI CLOSED
Trade ID  : SIM-20260505-001
Close     : [harga] ([alasan])
P&L       : [±pip] pip ([±nilai])
Posisi Aktif: [n] / 3
```
