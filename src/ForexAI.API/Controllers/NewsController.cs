using ForexAI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexAI.API.Controllers;

[ApiController]
[Route("api/news")]
public class NewsController : ControllerBase
{
    private readonly NewsCalendarService _news;

    public NewsController(NewsCalendarService news)
    {
        _news = news;
    }

    /// <summary>
    /// Upcoming economic calendar events. Filter currency optional —
    /// e.g. <c>?currency=USD,EUR</c> untuk EUR/USD trader.
    /// </summary>
    [HttpGet("upcoming")]
    public async Task<ActionResult<NewsResponse>> Upcoming(
        [FromQuery] int hours = 24,
        [FromQuery] string? currency = null,
        CancellationToken ct = default)
    {
        var (events, error) = await _news.GetUpcomingAsync(hours, ct);

        IEnumerable<NewsCalendarService.NewsEvent> filtered = events;
        if (!string.IsNullOrEmpty(currency))
        {
            var ccys = currency.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant()).ToHashSet();
            filtered = events.Where(e => ccys.Contains(e.Currency.ToUpperInvariant()));
        }

        var dtos = filtered.Select(e => new NewsEventDto(
            Title:    e.Title,
            Currency: e.Currency,
            Impact:   e.Impact,
            Time:     e.Time.ToString("o"),
            Forecast: e.Forecast,
            Previous: e.Previous,
            Actual:   e.Actual,
            MinutesUntil: (int)Math.Round((e.Time - DateTimeOffset.UtcNow).TotalMinutes)
        )).ToArray();

        return Ok(new NewsResponse(
            Events:     dtos,
            FetchError: error,
            FetchedAt:  DateTimeOffset.UtcNow.ToString("o")));
    }
}

public record NewsResponse(
    NewsEventDto[] Events,
    string? FetchError,
    string FetchedAt);

public record NewsEventDto(
    string Title,
    string Currency,
    string Impact,           // Low / Medium / High / Holiday
    string Time,             // ISO 8601
    string? Forecast,
    string? Previous,
    string? Actual,
    int MinutesUntil);       // negative = already passed (within last 30 min)
