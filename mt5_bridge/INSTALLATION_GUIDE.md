# Panduan Pemasangan ForexAI Bridge EA di MetaTrader 5

## Langkah 1: Copy File EA ke MT5

1. Buka aplikasi MetaTrader 5 (Exness).
2. Klik menu **File → Open Data Folder**.
3. Di folder yang terbuka, navigasi ke: `MQL5 / Experts /`
4. **Copy** file `ForexAI_Bridge.mq5` (dari folder `mt5_bridge/` di project ini) ke sana.

---

## Langkah 2: Izinkan WebRequest (WAJIB!)

Tanpa langkah ini, EA tidak bisa berkomunikasi dengan C#.

1. Di MT5, klik menu **Tools → Options**.
2. Buka tab **Expert Advisors**.
3. Centang: ☑ **Allow WebRequest for listed URL**.
4. Klik tombol **+** dan tambahkan URL berikut:
   ```
   http://127.0.0.1:5033
   ```
5. Klik **OK**.

---

## Langkah 3: Compile EA

1. Di MT5, klik menu **Tools → MetaEditor** (atau tekan F4).
2. Di MetaEditor, buka file `ForexAI_Bridge.mq5` yang sudah Anda copy.
3. Klik tombol **Compile** (F7 atau ikon palu di toolbar).
4. Pastikan di panel bawah muncul **"0 error(s), 0 warning(s)"**.

---

## Langkah 4: Pasang EA ke Chart

1. Kembali ke MT5.
2. Buka chart **EURUSD** dengan timeframe **M15**.
3. Di panel **Navigator** (biasanya di kiri), buka **Expert Advisors**.
4. **Drag** `ForexAI_Bridge` ke chart EURUSD M15.
5. Di jendela yang muncul, pastikan centang ☑ **Allow live trading**.
6. Klik **OK**.
7. Pastikan di pojok kanan chart muncul nama EA dan ikon wajah tersenyum 🙂 (bukan 😟).

---

## Langkah 5: Uji Koneksi

1. Pastikan backend C# Anda berjalan (jalankan `dotnet run` di folder `src/ForexAI.API`).
2. Di panel **Experts** atau **Journal** MT5, perhatikan log. Jika berhasil, Anda akan melihat:
   ```
   [ForexAI Bridge] ✅ Connected to C# backend successfully.
   ```
3. Di Dashboard, pilih broker **EXNESS** dan klik **Trigger New Analysis**.
4. Pantau panel Journal/Experts MT5 untuk melihat pesan seperti:
   ```
   [ForexAI Bridge] Received command: FETCH_DATA (id=xxxx)
   [ForexAI Bridge] FETCH_DATA callback status=200 (EURUSD/M15 100 candles)
   ```

---

## Troubleshooting

| Masalah | Solusi |
|---|---|
| EA menampilkan ikon 😟 | Aktifkan AutoTrading (tombol di toolbar MT5) |
| "Cannot reach C# backend" | Pastikan `dotnet run` sudah berjalan di terminal |
| "WebRequest is not allowed" | Ulangi Langkah 2 dan pastikan URL sudah ditambahkan |
| Error `4014` di log | URL di Langkah 2 belum ditambahkan dengan benar |
| Order tidak tereksekusi | Pastikan akun Exness Anda sudah login dan punya margin cukup |
