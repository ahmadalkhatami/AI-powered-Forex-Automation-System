using System.Text.Json;
using System.Text.Json.Serialization;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using ForexAI.Domain.ValueObjects;
using ForexAI.Infrastructure.Persistence.Dtos;

namespace ForexAI.Infrastructure.Persistence.Repositories;

public class JsonTradePositionRepository : ITradePositionRepository
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonTradePositionRepository()
    {
        _filePath = ResolveDefaultPath();
    }

    public JsonTradePositionRepository(string filePath)
    {
        _filePath = filePath;
    }

    private static string ResolveDefaultPath() =>
        Path.Combine(ProjectPaths.ImplementationArtifactsDir, "execution-log.json");

    private async Task<List<TradePositionDto>> LoadAllAsync()
    {
        if (!File.Exists(_filePath))
            return new List<TradePositionDto>();

        var json = await File.ReadAllTextAsync(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<TradePositionDto>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("execution_log", out var legacyArray))
            return ParseLegacyFormat(legacyArray);

        if (root.TryGetProperty("positions", out var positionsArray))
        {
            var dtos = JsonSerializer.Deserialize<List<TradePositionDto>>(
                positionsArray.GetRawText(), JsonOptions);
            return dtos ?? new List<TradePositionDto>();
        }

        return new List<TradePositionDto>();
    }

    private static List<TradePositionDto> ParseLegacyFormat(JsonElement array)
    {
        var result = new List<TradePositionDto>();
        foreach (var item in array.EnumerateArray())
        {
            result.Add(new TradePositionDto
            {
                TradeId = GetString(item, "trade_id"),
                RunId = GetString(item, "run_id"),
                Status = GetString(item, "status", "SKIPPED"),
                Pair = GetString(item, "pair"),
                Direction = GetStringOrNull(item, "direction") ?? "HOLD",
                Entry = GetDecimal(item, "entry_price"),
                StopLoss = GetDecimal(item, "stop_loss"),
                TakeProfit = GetDecimal(item, "take_profit"),
                LotSize = GetDecimal(item, "lot_size"),
                RiskAmount = GetDecimal(item, "risk_amount"),
                PotentialProfit = GetDecimal(item, "potential_profit"),
                RiskReward = GetDecimal(item, "risk_reward"),
                FloatingPnl = GetDecimal(item, "floating_pnl"),
                FloatingPnlPips = GetInt(item, "floating_pnl_pips"),
                Mode = GetString(item, "mode", "SIMULATION"),
                SkipReason = GetStringOrNull(item, "skip_reason")
            });
        }
        return result;
    }

    private async Task SaveAllAsync(List<TradePositionDto> dtos)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var wrapper = new { positions = dtos };
        var json = JsonSerializer.Serialize(wrapper, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<TradePosition?> GetActiveByPairAsync(string pair)
    {
        var all = await LoadAllAsync();
        // Normalize by removing '/' so "EURUSD" matches stored "EUR/USD" and vice versa
        var normalizedQuery = pair.Replace("/", "").ToUpperInvariant();
        var dto = all.FirstOrDefault(d =>
            d.Pair.Replace("/", "").Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));
        return dto is null ? null : DtoMapper.ToDomain(dto);
    }

    public async Task<IReadOnlyList<TradePosition>> GetOpenPositionsAsync()
    {
        var all = await LoadAllAsync();
        return all
            .Where(d => string.Equals(d.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .Select(DtoMapper.ToDomain)
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<TradePosition>> GetAllAsync()
    {
        var all = await LoadAllAsync();
        return all.Select(DtoMapper.ToDomain).ToList().AsReadOnly();
    }

    public async Task SaveAsync(TradePosition position)
    {
        var all = await LoadAllAsync();
        var dto = DtoMapper.ToDto(position);
        var idx = all.FindIndex(d =>
            string.Equals(d.TradeId, position.TradeId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            all[idx] = dto;
        else
            all.Add(dto);
        await SaveAllAsync(all);
    }

    public async Task<int> CountOpenPositionsAsync()
    {
        var all = await LoadAllAsync();
        return all.Count(d =>
            string.Equals(d.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveManyAsync(IEnumerable<TradePosition> positions)
    {
        var all = await LoadAllAsync();
        foreach (var position in positions)
        {
            var dto = DtoMapper.ToDto(position);
            var idx = all.FindIndex(d =>
                string.Equals(d.TradeId, position.TradeId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                all[idx] = dto;
            else
                all.Add(dto);
        }
        await SaveAllAsync(all);
    }

    public async Task<DailyRiskUsage> GetDailyRiskUsageAsync(DateTimeOffset asOfUtc)
    {
        var all = await LoadAllAsync();

        // UTC day boundary: 00:00 UTC = 07:00 WIB. Trades dengan OpenedAt < dayStart
        // dianggap "kemarin" dan tidak masuk hitungan.
        var dayStart = new DateTimeOffset(asOfUtc.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd   = dayStart.AddDays(1);

        var today = all.Where(d =>
            d.OpenedAt.HasValue                                         &&
            d.OpenedAt.Value >= dayStart                                &&
            d.OpenedAt.Value <  dayEnd                                  &&
            !string.Equals(d.Status, "SKIPPED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        decimal usedUsd = today.Sum(d => d.RiskAmount);
        int     count   = today.Count;

        return new DailyRiskUsage(usedUsd, count, asOfUtc);
    }

    private static string GetString(JsonElement el, string prop, string fallback = "")
    {
        return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
    }

    private static string? GetStringOrNull(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static decimal GetDecimal(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var v) &&
               v.ValueKind == JsonValueKind.Number &&
               v.TryGetDecimal(out var d)
            ? d
            : 0m;
    }

    private static int GetInt(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var v) &&
               v.ValueKind == JsonValueKind.Number &&
               v.TryGetInt32(out var i)
            ? i
            : 0;
    }
}
