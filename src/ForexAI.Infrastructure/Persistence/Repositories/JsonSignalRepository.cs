using System.Text.Json;
using System.Text.Json.Serialization;
using ForexAI.Domain.Entities;
using ForexAI.Domain.Interfaces;
using ForexAI.Infrastructure.Persistence.Dtos;

namespace ForexAI.Infrastructure.Persistence.Repositories;

public class JsonSignalRepository : ISignalRepository
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonSignalRepository()
    {
        _filePath = ResolveDefaultPath();
    }

    public JsonSignalRepository(string filePath)
    {
        _filePath = filePath;
    }

    private static string ResolveDefaultPath()
    {
        var current = Directory.GetCurrentDirectory();
        return Path.GetFullPath(
            Path.Combine(current, "_bmad-output/implementation-artifacts/signal-history.json"));
    }

    private async Task<List<TradeSignalDto>> LoadAllAsync()
    {
        if (!File.Exists(_filePath))
            return new List<TradeSignalDto>();

        var json = await File.ReadAllTextAsync(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<TradeSignalDto>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("signals", out var signalsArray))
        {
            var dtos = JsonSerializer.Deserialize<List<TradeSignalDto>>(
                signalsArray.GetRawText(), JsonOptions);
            return dtos ?? new List<TradeSignalDto>();
        }

        return new List<TradeSignalDto>();
    }

    private async Task SaveAllAsync(List<TradeSignalDto> dtos)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var wrapper = new { signals = dtos };
        var json = JsonSerializer.Serialize(wrapper, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<TradeSignal?> GetLatestAsync(string pair)
    {
        var all = await LoadAllAsync();
        var dto = all
            .Where(d => string.Equals(d.Pair, pair, StringComparison.OrdinalIgnoreCase))
            .MaxBy(d => d.Timestamp);
        return dto is null ? null : DtoMapper.ToDomain(dto);
    }

    public async Task<TradeSignal?> GetByIdAsync(Guid id)
    {
        var all = await LoadAllAsync();
        var dto = all.FirstOrDefault(d => d.Id == id);
        return dto is null ? null : DtoMapper.ToDomain(dto);
    }

    public async Task SaveAsync(TradeSignal signal)
    {
        var all = await LoadAllAsync();
        all.Add(DtoMapper.ToDto(signal));
        await SaveAllAsync(all);
    }
}
