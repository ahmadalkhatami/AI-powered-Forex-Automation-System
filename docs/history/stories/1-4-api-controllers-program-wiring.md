# Story 1.4: API Controllers and Program.cs Wiring

Status: review

## Story

As a frontend developer,
I want a minimal REST API with 4 endpoints wired to MediatR handlers,
so that the React dashboard can trigger analysis, evaluate risk, execute trades, and query position status via HTTP.

## Acceptance Criteria

1. `Program.cs` in `src/ForexAI.API/`:
   - Calls `services.AddApplication()` and `services.AddInfrastructure()`
   - Adds CORS policy allowing `http://localhost:3000` (Next.js dev) and `http://localhost:3001`
   - Adds Swagger/OpenAPI in Development environment only
   - Configures `System.Text.Json` with `JsonStringEnumConverter` and camelCase globally
2. `ForexAI.Infrastructure/DependencyInjection.cs` fully registers all infrastructure services (all stories 1.1–1.3 registrations in one place)
3. Four controller endpoints:
   - `POST /api/signal/analyze` — body: `{ "pair": "EURUSD", "timeframe": "M15" }` → returns `TradeSignal` JSON
   - `POST /api/risk/evaluate` — body: `EvaluateRiskRequest` → returns `RiskValidation` JSON
   - `POST /api/trade/execute` — body: `ExecuteTradeRequest` → returns `TradePosition` JSON
   - `GET /api/position/{pair}` — returns `TradePosition?` JSON (204 if null)
4. All endpoints return HTTP 200 on success, 400 on validation failure, 500 on unhandled exception
5. Global exception handler middleware returns `{ "error": "message" }` JSON — no raw stack traces in responses
6. `dotnet run --project src/ForexAI.API` starts successfully, Swagger UI at `http://localhost:5000/swagger`

## Tasks / Subtasks

- [x] Create `Program.cs` (AC: 1)
  - [x] Register services (AddApplication, AddInfrastructure)
  - [x] Add CORS for localhost:3000
  - [x] Add Swagger in Development
  - [x] Configure global JSON options (camelCase + enum strings)
  - [x] Add global exception handler middleware
- [x] Create request/response DTOs in `src/ForexAI.API/Models/` (AC: 3)
  - [x] `AnalyzeSignalRequest.cs` — `{ Pair, Timeframe }`
  - [x] `EvaluateRiskRequest.cs` — `{ SignalId, FinalDecision, AdjustedConfidence, TotalScore, AgreementScore, Equity, OpenPositions }`
  - [x] `ExecuteTradeRequest.cs` — `{ SignalId, RiskValidation, PeakEquity, CurrentEquity, Mode }`
- [x] Create `SignalController.cs` (AC: 3)
- [x] Create `RiskController.cs` (AC: 3)
- [x] Create `TradeController.cs` (AC: 3)
- [x] Create `PositionController.cs` (AC: 3)
- [x] Verify `dotnet run` starts and Swagger loads (AC: 6)

## Dev Notes

### Program.cs Pattern (Minimal API style, .NET 8)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:3000", "http://localhost:3001")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(appBuilder => appBuilder.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
}));

app.UseCors();
app.MapControllers();
app.Run();
```

### Controller Pattern

All controllers follow the same thin pattern — no business logic, just MediatR dispatch:

```csharp
[ApiController]
[Route("api/signal")]
public class SignalController : ControllerBase
{
    private readonly IMediator _mediator;
    public SignalController(IMediator mediator) => _mediator = mediator;

    [HttpPost("analyze")]
    public async Task<ActionResult<TradeSignal>> Analyze(
        [FromBody] AnalyzeSignalRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AnalyzeSignalCommand(request.Pair, request.Timeframe), ct);
        return Ok(result);
    }
}
```

### EvaluateRiskRequest Shape

The frontend will send a `PredictorResult` inline in the request. Keep it flat:

```csharp
public record EvaluateRiskRequest(
    Guid SignalId,
    decimal AdjustedConfidence,
    int TotalScore,
    decimal AgreementScore,
    string FinalDecision,       // "BUY" | "SELL" | "HOLD"
    decimal Equity,
    int OpenPositions
);
```

Map to `EvaluateRiskCommand` by constructing `PredictorResult` from these fields. This avoids exposing domain types directly in the API contract.

### GET /api/position/{pair} — 204 on Null

```csharp
[HttpGet("{pair}")]
public async Task<ActionResult<TradePosition>> GetStatus(string pair, CancellationToken ct)
{
    var result = await _mediator.Send(new GetPositionStatusQuery(pair), ct);
    return result is null ? NoContent() : Ok(result);
}
```

### ForexAI.API.csproj — Add MediatR Package

The API csproj currently only has Swashbuckle and OpenAPI. MediatR is already in Application project and will be pulled in transitively via ProjectReference, but confirm MediatR is accessible. If not, add:

```xml
<PackageReference Include="MediatR" Version="12.4.1" />
```

### Project Structure Notes

New files:
```
src/ForexAI.API/
├── Program.cs                              NEW
├── Controllers/
│   ├── SignalController.cs                 NEW
│   ├── RiskController.cs                   NEW
│   ├── TradeController.cs                  NEW
│   └── PositionController.cs              NEW
└── Models/
    ├── AnalyzeSignalRequest.cs             NEW
    ├── EvaluateRiskRequest.cs              NEW
    └── ExecuteTradeRequest.cs              NEW
```

### References

- [Source: src/ForexAI.Application/DependencyInjection.cs] — AddApplication() pattern
- [Source: src/ForexAI.Application/UseCases/AnalyzeSignal/AnalyzeSignalCommand.cs]
- [Source: src/ForexAI.Application/UseCases/EvaluateRisk/EvaluateRiskCommand.cs]
- [Source: src/ForexAI.Application/UseCases/ExecuteTrade/ExecuteTradeCommand.cs]
- [Source: src/ForexAI.Application/UseCases/GetPositionStatus/GetPositionStatusQuery.cs]
- [Source: src/ForexAI.API/ForexAI.API.csproj] — existing packages

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ `EvaluateRiskRequest` dibuat flat (tidak embed `PredictorResult` langsung) — controller merekonstruksi `PredictorResult` dari field individual
- ✅ `ExecuteTradeRequest` embed `RiskValidation` domain type langsung — camelCase + `JsonStringEnumConverter` global memastikan serialize/deserialize benar
- ✅ `public partial class Program { }` ditambahkan di akhir Program.cs — required untuk `WebApplicationFactory<Program>` di integration tests (Story 1.5)
- ✅ MediatR tersedia transitively via Application project reference — tidak perlu tambah package di API.csproj
- ✅ Build: 4 projects, 0 errors, 0 warnings

### File List

- `src/ForexAI.API/Program.cs` — NEW
- `src/ForexAI.API/Models/AnalyzeSignalRequest.cs` — NEW
- `src/ForexAI.API/Models/EvaluateRiskRequest.cs` — NEW
- `src/ForexAI.API/Models/ExecuteTradeRequest.cs` — NEW
- `src/ForexAI.API/Controllers/SignalController.cs` — NEW
- `src/ForexAI.API/Controllers/RiskController.cs` — NEW
- `src/ForexAI.API/Controllers/TradeController.cs` — NEW
- `src/ForexAI.API/Controllers/PositionController.cs` — NEW
