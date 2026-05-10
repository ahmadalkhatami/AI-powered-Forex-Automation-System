# Story 1.5: End-to-End Pipeline Smoke Test

Status: review

## Story

As a developer,
I want a simple integration test that exercises the full pipeline through the API,
so that I can verify Domain + Application + Infrastructure + API all wire together correctly before frontend work begins.

## Acceptance Criteria

1. A `tests/ForexAI.Integration/` xUnit project exists with one test class `PipelineIntegrationTests`
2. Test `FullPipeline_AnalyzeEvaluateExecute_CreatesActivePosition` runs successfully:
   - Step 1: `POST /api/signal/analyze` with `{ "pair": "EURUSD", "timeframe": "M15" }` → HTTP 200, returns TradeSignal with `signal: "BUY"`
   - Step 2: `POST /api/risk/evaluate` using SignalId from step 1 → HTTP 200, returns `decision: "GO"` or `"GO_WITH_CAUTION"`
   - Step 3: `POST /api/trade/execute` with SignalId + RiskValidation from step 2 → HTTP 200, returns TradePosition with `status: "ACTIVE"`
   - Step 4: `GET /api/position/EURUSD` → HTTP 200, returns same active position
3. After test run, `_bmad-output/implementation-artifacts/execution-log.json` contains the new position
4. Test is runnable with `dotnet test tests/ForexAI.Integration/`

## Tasks / Subtasks

- [x] Create integration test project (AC: 1)
  - [x] `dotnet new xunit -n ForexAI.Integration -o tests/ForexAI.Integration`
  - [x] Add PackageReference: `Microsoft.AspNetCore.Mvc.Testing`
  - [x] Add ProjectReference to `ForexAI.API`
- [x] Create `WebApplicationFactory` test host (AC: 2)
  - [x] Override `CreateHostBuilder` to use test environment
- [x] Write `PipelineIntegrationTests` (AC: 2)
  - [x] 4-step sequential test using `HttpClient`
  - [x] Assert HTTP status codes and JSON response shapes
- [x] Verify `execution-log.json` updated (AC: 3)
- [x] Confirm `dotnet test` passes (AC: 4)

## Dev Notes

### WebApplicationFactory Pattern (.NET 8)

```csharp
public class ForexApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}

public class PipelineIntegrationTests : IClassFixture<ForexApiFactory>
{
    private readonly HttpClient _client;

    public PipelineIntegrationTests(ForexApiFactory factory)
    {
        _client = factory.CreateClient();
    }
}
```

### Program.cs Must Be Public-Accessible

For `WebApplicationFactory<Program>` to work, `Program` class must be accessible. In .NET 8 minimal API, add this to `Program.cs`:

```csharp
// At the very end of Program.cs
public partial class Program { }
```

### Test Structure

```csharp
[Fact]
public async Task FullPipeline_AnalyzeEvaluateExecute_CreatesActivePosition()
{
    // Step 1: Analyze
    var analyzeResp = await _client.PostAsJsonAsync("/api/signal/analyze",
        new { pair = "EURUSD", timeframe = "M15" });
    analyzeResp.EnsureSuccessStatusCode();
    var signal = await analyzeResp.Content.ReadFromJsonAsync<TradeSignalDto>();
    Assert.Equal("BUY", signal!.Signal);

    // Step 2: Evaluate risk
    var evaluateResp = await _client.PostAsJsonAsync("/api/risk/evaluate",
        new {
            signalId = signal.Id,
            adjustedConfidence = 0.69,
            totalScore = 83,
            agreementScore = 0.93,
            finalDecision = "BUY",
            equity = 10000,
            openPositions = 0
        });
    evaluateResp.EnsureSuccessStatusCode();
    var risk = await evaluateResp.Content.ReadFromJsonAsync<RiskValidationDto>();
    Assert.True(risk!.IsGo);

    // Step 3: Execute
    var executeResp = await _client.PostAsJsonAsync("/api/trade/execute",
        new {
            signalId = signal.Id,
            riskValidation = risk,
            peakEquity = 10000,
            currentEquity = 10000,
            mode = "SIMULATION"
        });
    executeResp.EnsureSuccessStatusCode();
    var position = await executeResp.Content.ReadFromJsonAsync<TradePositionDto>();
    Assert.Equal("ACTIVE", position!.Status);

    // Step 4: Query
    var statusResp = await _client.GetAsync("/api/position/EURUSD");
    Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
}
```

### Simple DTO Records for Test Assertions

Create simple record types in the test project for response deserialization — do NOT reference domain types:

```csharp
// In tests/ForexAI.Integration/Dtos/
record TradeSignalDto(Guid Id, string Signal, decimal ConfidenceScore);
record RiskValidationDto(string Decision, bool IsGo, TradeParametersDto? ValidatedParameters);
record TradeParametersDto(decimal Entry, decimal StopLoss, decimal TakeProfit, decimal LotSize, decimal RiskAmount);
record TradePositionDto(string TradeId, string Status, string Pair);
```

### csproj for Integration Test Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ForexAI.API\ForexAI.API.csproj" />
  </ItemGroup>
</Project>
```

### Project Structure Notes

New directory and files:
```
tests/
└── ForexAI.Integration/
    ├── ForexAI.Integration.csproj      NEW
    ├── ForexApiFactory.cs              NEW
    ├── PipelineIntegrationTests.cs     NEW
    └── Dtos/
        └── TestDtos.cs                 NEW
```

### References

- [Source: src/ForexAI.API/Program.cs] — must expose `Program` partial class
- [Source: _bmad-output/planning-artifacts/signal-output.json] — expected BUY signal
- [Source: _bmad-output/planning-artifacts/risk-decision.json] — expected GO decision

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ `WebApplicationFactory` membutuhkan `*.sln` file untuk resolve content root — dibuat `ForexAI.sln` minimal di project root
- ✅ API project ada di `src/ForexAI.API/` (bukan root `/ForexAI.API/`) — `ConfigureWebHost` override `UseContentRoot` ke path yang benar
- ✅ Stub services (`StubMarketDataService`, `BmadSignalAnalyzer`) resolve `_bmad-output/` via `Directory.GetCurrentDirectory()` — `ForexApiFactory` static constructor set CWD ke project root sebelum host dibuat
- ✅ Pair format: BMAD JSON menyimpan `"EUR/USD"` (dengan slash), tapi API query pakai `"EURUSD"` — tambahkan normalisasi di `GetActiveByPairAsync` (remove `/` sebelum compare)
- ✅ `RiskValidationDto` di test perlu match full shape `RiskValidation` domain type (termasuk `positionDecision`, `cautionNotes`, `noGoReasons`) untuk round-trip serialize ke `ExecuteTradeRequest`
- ✅ Build: 5 projects, 0 errors, 0 warnings
- ✅ Test: 1 passed, 0 failed — full pipeline berjalan end-to-end

### File List

- `tests/ForexAI.Integration/ForexAI.Integration.csproj` — NEW
- `tests/ForexAI.Integration/ForexApiFactory.cs` — NEW
- `tests/ForexAI.Integration/PipelineIntegrationTests.cs` — NEW
- `tests/ForexAI.Integration/Dtos/TestDtos.cs` — NEW
- `ForexAI.sln` — NEW (minimal solution file, required by WebApplicationFactory)
- `src/ForexAI.Infrastructure/Persistence/Repositories/JsonTradePositionRepository.cs` — MODIFIED (pair normalization in GetActiveByPairAsync)
