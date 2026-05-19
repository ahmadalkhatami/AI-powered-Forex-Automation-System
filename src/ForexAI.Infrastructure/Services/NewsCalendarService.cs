using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Fetch + cache forex economic calendar events dari public XML feed.
/// Default source: Forex Factory weekly XML (faireconomy.media mirror).
/// User bisa override URL via appsettings: <c>News:FeedUrl</c>.
///
/// <para>Cache 30 menit untuk avoid hit external service tiap request.
/// Graceful fallback ke empty list kalau feed down — sistem tetap jalan tanpa news.</para>
/// </summary>
public class NewsCalendarService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private const string DefaultFeedUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.xml";

    public record NewsEvent(
        string Title,
        string Currency,
        string Impact,       // Low / Medium / High
        DateTimeOffset Time,
        string? Forecast,
        string? Previous,
        string? Actual);

    private readonly IHttpClientFactory _http;
    private readonly ILogger<NewsCalendarService> _log;
    private readonly string _feedUrl;
    private readonly object _lock = new();
    private List<NewsEvent> _cache = new();
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private string? _lastError;

    public NewsCalendarService(
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<NewsCalendarService> log)
    {
        _http    = http;
        _log     = log;
        _feedUrl = config["News:FeedUrl"] ?? DefaultFeedUrl;
    }

    /// <summary>Return events dalam window (hoursAhead jam ke depan). Triggers refresh kalau cache expired.</summary>
    public async Task<(IReadOnlyList<NewsEvent> events, string? error)> GetUpcomingAsync(int hoursAhead = 24, CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _cachedAt > CacheTtl)
            await RefreshAsync(ct);

        lock (_lock)
        {
            var until = DateTimeOffset.UtcNow.AddHours(hoursAhead);
            var upcoming = _cache
                .Where(e => e.Time >= DateTimeOffset.UtcNow.AddMinutes(-30) && e.Time <= until)
                .OrderBy(e => e.Time)
                .ToList();
            return (upcoming, _lastError);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var xml = await client.GetStringAsync(_feedUrl, ct);
            var parsed = ParseFeed(xml);
            lock (_lock)
            {
                _cache = parsed;
                _cachedAt = DateTimeOffset.UtcNow;
                _lastError = null;
            }
            _log.LogInformation("News calendar refresh OK — {Count} events", parsed.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "News calendar refresh failed — using cached data ({CacheAge:F0} min old)",
                (DateTimeOffset.UtcNow - _cachedAt).TotalMinutes);
            lock (_lock) _lastError = ex.Message;
        }
    }

    /// <summary>
    /// Parse Forex Factory XML format. Each event tag punya:
    /// title, country (currency code), date, time, impact (1/2/3 atau Low/Medium/High),
    /// forecast, previous, actual.
    /// </summary>
    private static List<NewsEvent> ParseFeed(string xml)
    {
        var events = new List<NewsEvent>();
        var doc = XDocument.Parse(xml);

        // Forex Factory format: <weeklyevents><event>...</event></weeklyevents>
        foreach (var el in doc.Descendants("event"))
        {
            string title    = el.Element("title")?.Value?.Trim() ?? "";
            string currency = el.Element("country")?.Value?.Trim() ?? "";
            string impactRaw = el.Element("impact")?.Value?.Trim() ?? "";
            string dateStr  = el.Element("date")?.Value?.Trim() ?? "";
            string timeStr  = el.Element("time")?.Value?.Trim() ?? "";
            string? forecast = el.Element("forecast")?.Value?.Trim();
            string? previous = el.Element("previous")?.Value?.Trim();
            string? actual   = el.Element("actual")?.Value?.Trim();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(currency)) continue;

            // Date format: MM-dd-yyyy (Forex Factory US format)
            // Time format: "1:30am" / "2:00pm" / "All Day" / "Tentative"
            if (!DateTime.TryParseExact(dateStr, "MM-dd-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
                continue;

            DateTimeOffset eventTime;
            if (timeStr.Equals("All Day", StringComparison.OrdinalIgnoreCase) ||
                timeStr.Equals("Tentative", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(timeStr))
            {
                eventTime = new DateTimeOffset(date, TimeSpan.Zero);
            }
            else if (DateTime.TryParseExact(timeStr, new[] { "h:mmtt", "htt" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsedTime))
            {
                var combined = date.Add(parsedTime.TimeOfDay);
                // Forex Factory time biasanya EST/EDT (UTC-5/-4). Asumsi UTC-5 untuk simplifikasi.
                eventTime = new DateTimeOffset(combined, TimeSpan.FromHours(-5)).ToUniversalTime();
            }
            else
            {
                continue;
            }

            // Normalize impact
            string impact = impactRaw switch
            {
                "High" => "High",
                "Medium" => "Medium",
                "Low" => "Low",
                "Holiday" => "Holiday",
                _ => "Low"
            };

            events.Add(new NewsEvent(
                Title:    title,
                Currency: currency,
                Impact:   impact,
                Time:     eventTime,
                Forecast: string.IsNullOrEmpty(forecast) ? null : forecast,
                Previous: string.IsNullOrEmpty(previous) ? null : previous,
                Actual:   string.IsNullOrEmpty(actual)   ? null : actual));
        }

        return events.OrderBy(e => e.Time).ToList();
    }
}
