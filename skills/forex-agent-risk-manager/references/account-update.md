---
capability: account-update
---

# Update Kondisi Akun

Terima update equity, posisi terbuka, atau hasil trade dari user dan perbarui sanctum.

## Yang Bisa Diupdate

**Equity baru:** Update current equity, hitung drawdown terbaru dari peak equity. Jika peak equity baru terlampaui, update juga peak equity.

**Posisi closed:** Catat hasil (profit/loss pip dan nilai), update equity, catat di session log.

**Posisi baru terbuka:** Konfirmasi bahwa ini sesuai parameter yang disetujui Risk Gate. Update jumlah posisi terbuka.

## Setelah Update

Tampilkan ringkasan kondisi akun terkini:

```
[KONDISI AKUN TERKINI]
Equity Saat Ini  : [nilai]
Peak Equity      : [nilai]
Drawdown Kini    : [%] (Limit: 10%)
Posisi Terbuka   : [n] / 3
Status Sistem    : AKTIF / PAUSE (mendekati limit) / STOP (limit tercapai)
```

Jika drawdown ≥ 8%, berikan peringatan proaktif bahwa sistem mendekati batas dan rekomendasikan untuk mengurangi frekuensi trading.
Jika drawdown ≥ 10%, deklarasikan SYSTEM STOP dan jangan proses sinyal baru sampai user secara eksplisit mereset kondisi.
