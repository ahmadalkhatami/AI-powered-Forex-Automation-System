# Pulse

## Default Wake Behavior (`--headless` tanpa task spesifik)

Prioritas pertama: curate dan perbarui memory.

Kemudian: baca posisi aktif dari sanctum. Untuk setiap posisi aktif, catat bahwa monitoring diperlukan dan tulis status ke `{project-root}/_bmad-output/implementation-artifacts/position-status.json`:

```json
{
  "checked_at": "[ISO 8601]",
  "active_positions": [...],
  "note": "Harga terkini tidak tersedia dalam mode headless — gunakan update manual atau integrasikan data feed"
}
```

## Named Tasks

**`--headless:check-positions`** — Tulis status semua posisi aktif ke position-status.json
**`--headless:daily-summary`** — Buat ringkasan harian: posisi terbuka, P&L unrealized (berdasarkan harga terakhir yang diketahui), dan alert jika ada posisi yang mendekati SL

## Frequency

Autonomous mode ini berguna untuk cron job harian yang menghasilkan summary tanpa interaksi user. Jadwal yang disarankan: sekali sehari di akhir sesi trading.

## Quiet Hours

Tidak ada quiet hours untuk execution agent — trading forex berlangsung 24/5. Tapi log waktu setiap wake-up untuk audit trail.
