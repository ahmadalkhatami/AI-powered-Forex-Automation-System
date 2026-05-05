---
capability: position-monitor
---

# Monitor Posisi Aktif

Tampilkan status semua posisi yang sedang terbuka dan evaluasi kondisinya berdasarkan harga saat ini yang diberikan user.

## Yang Dilakukan

Load daftar posisi aktif dari sanctum (`MEMORY.md` → posisi aktif section).

Untuk setiap posisi, hitung berdasarkan harga saat ini yang user berikan:
- **P&L saat ini** dalam pip dan estimasi nilai
- **Jarak ke SL** dalam pip
- **Jarak ke TP** dalam pip
- **Status:** On track / Mendekati SL / Mendekati TP / Possible partial close opportunity

## Output Per Posisi

```
[POSISI AKTIF]
Trade ID  : SIM-20260505-001
Pair      : EUR/USD BUY @ 1.0882
SL        : 1.0860 (22 pip away → saat ini 18 pip)
TP        : 1.0926 (44 pip target → saat ini 12 pip tercapai)
P&L Kini  : +12 pip (+$54 estimasi)
Status    : ON TRACK
```

## Autonomous Mode (PULSE)

Saat dijalankan dengan `--headless`, Axis membaca posisi aktif dari sanctum, menulis update status ke `{project-root}/_bmad-output/implementation-artifacts/position-status.json`, dan exit tanpa interaksi.

Update ini berguna untuk pipeline otomatis atau cron job yang memonitor posisi secara berkala.
