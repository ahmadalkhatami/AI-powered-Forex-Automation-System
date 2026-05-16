# Development Guide

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | **8.0+** | `dotnet --version` |
| Node.js | 20+ | Frontend |
| MetaTrader 5 | latest | Optional — untuk live data + execution. Bisa pakai wine di macOS/Linux. |
| MIFX demo/real account | — | Sumber market data utama |

---

## Getting Started

```bash
git clone <repo>
cd AI-powered-Forex-Automation-System

# 1. Build solution
dotnet build ForexAI.sln

# 2. Start backend API
dotnet run --project src/ForexAI.API
# → http://localhost:8080  (Swagger: /swagger)

# 3. Start frontend dashboard (terminal lain)
cd frontend
npm install
npm run dev
# → http://localhost:3000
```

Tanpa MT5 EA running, endpoint signal analysis return error (`InvalidOperationException: EA not connected`). Untuk smoke test tanpa EA, jalankan integration test (lihat di bawah).

---

## Project Structure

```
AI-powered-Forex-Automation-System/
├── src/
│   ├── ForexAI.Domain/             # Entities, value objects, interfaces
│   ├── ForexAI.Application/        # Use cases (no infra deps)
│   ├── ForexAI.Infrastructure/     # Mifx EA bridge, JSON repos, services
│   └── ForexAI.API/                # ASP.NET Core controllers + SignalR hub
├── tests/
│   └── ForexAI.Integration/        # xUnit + WebApplicationFactory
├── frontend/                       # Next.js 14 dashboard
├── mql5/                           # MetaTrader 5 Expert Advisor
├── data/                           # Runtime data (mostly gitignored)
│   ├── mode-state.json
│   ├── demo/                       # Demo trade history
│   └── real/                       # Real trade history
├── docs/                           # Wiki
│   └── history/                    # Story specs Epic 1-4 (historical)
├── scripts/                        # Bash helper (e.g. setup-mt5.sh)
├── ForexAI.sln
└── CLAUDE.md                       # AI agent instructions
```

---

## Running Tests

```bash
# All tests
dotnet test ForexAI.sln

# Integration test verbose
dotnet test tests/ForexAI.Integration/ --logger "console;verbosity=normal"
```

[`PipelineIntegrationTests`](../tests/ForexAI.Integration/PipelineIntegrationTests.cs) menjalankan full pipeline di in-memory test host. Test pakai `FakeMarketDataService` ([tests/ForexAI.Integration/FakeMarketDataService.cs](../tests/ForexAI.Integration/FakeMarketDataService.cs)) — deterministic bullish setup. Tidak perlu MT5 EA running.

Repositories di-replace dengan instance temp-file (`Path.GetTempFileName()`) supaya tidak bleed ke `data/demo/execution-log.json`.

---

## MT5 Expert Advisor Setup

Source: [mql5/ForexAI_Bridge.mq5](../mql5/ForexAI_Bridge.mq5), compiled: `ForexAI_Bridge.ex5`.

### Manual deploy

1. Copy `.mq5` + `.ex5` ke `<MT5_TERMINAL>/MQL5/Experts/`
2. Restart MT5 (atau Navigator → Refresh)
3. Attach EA ke chart EURUSD M15
4. Enable **Allow algo trading** + **Allow WebRequest for listed URL** → tambahkan `http://localhost:8080`
5. EA mulai push tick + candle ke `POST /api/mifx-bridge/tick`, `/api/mifx-bridge/candle`, dst.

### Automated deploy

```bash
curl -X POST http://localhost:8080/api/ea/deploy
```

`EaDeployService` cari MT5 terminal folder (Windows path biasa: `%APPDATA%/MetaQuotes/Terminal/<hash>/MQL5/Experts/`), copy file, optionally trigger compile via MetaEditor CLI.

`scripts/setup-mt5.sh` — helper Bash untuk macOS wine setup.

---

## Adding a New Use Case

1. **Domain** — kalau perlu interface baru, tambah di [src/ForexAI.Domain/Interfaces/](../src/ForexAI.Domain/Interfaces/)
2. **Application** — bikin folder baru di [src/ForexAI.Application/UseCases/NewFeature/](../src/ForexAI.Application/UseCases/) berisi request + handler
3. **Infrastructure** — implement interface baru, register di [DependencyInjection.cs](../src/ForexAI.Infrastructure/DependencyInjection.cs)
4. **API** — tambah endpoint di controller existing atau bikin controller baru di [src/ForexAI.API/Controllers/](../src/ForexAI.API/Controllers/)

---

## Adding a New Broker Adapter

Implement `IBrokerService`:

```csharp
public class MyBrokerService : IBrokerService
{
    public Task<BrokerExecutionResult> PlaceOrderAsync(TradeParameters p) { ... }
    public Task ClosePositionAsync(string tradeId) { ... }
    public Task<BrokerAccountStatus> GetAccountStatusAsync() { ... }
}
```

Register di `DependencyInjection.AddInfrastructure()`, swap dari `MifxBrokerService` ke implementasi baru, atau gate via feature flag.

Folder existing untuk reference: `src/ForexAI.Infrastructure/Services/Deriv/` dan `Services/Exness/` (tidak aktif di DI default).

---

## Debugging Tips

### EA tidak push data ke API

- Cek MT5 **Experts** log tab — error WebRequest biasanya soal allowed URL whitelist
- Cek `data/demo/audit-log.jsonl` untuk lihat event terakhir yang ter-log
- `GET /api/mifx-bridge/health` — return last tick timestamp; kalau > 1 menit, EA disconnect

### Signal analysis return 503

`InvalidOperationException` artinya EA belum push tick/candle pertama. Tunggu EA on-bar handler trigger (max 1 candle M15 = 15 menit), atau pakai `BacktestRunner` untuk replay history.

### Frontend tidak terima SignalR push

- CORS — pastikan port frontend di `Program.cs` CORS whitelist (saat ini: 3000, 3001)
- SignalR hub URL: `http://localhost:8080/hub/dashboard`
- Network tab: cek negotiate handshake 200

### Test failing dengan DirectoryNotFoundException

Test factory butuh content root absolut. Pastikan `ForexApiFactory.FindProjectRoot()` ketemu folder `data/`. Kalau project di-rename, update path matching di `FindProjectRoot()`.

---

## Risk Management — Hard Limits

Enforced di [`RuleBasedRiskEvaluator`](../src/ForexAI.Infrastructure/Services/RuleBasedRiskEvaluator.cs) dan `ExecuteTradeHandler`. **Jangan bypass.**

| Invariant | Value |
|---|---|
| Risk per trade | **1%** equity (Nano tier ada $ cap tambahan) |
| Max drawdown | **10%** → sistem auto-STOP |
| Max open positions | **3** |
| Min AI confidence | **60** |
| Max trade/hari | **7** |

Lihat [pipeline.md](pipeline.md) untuk flow detail dan [architecture.md](architecture.md) untuk layer responsibilities.
