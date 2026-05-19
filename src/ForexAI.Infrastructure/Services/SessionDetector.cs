namespace ForexAI.Infrastructure.Services;

/// <summary>
/// Detect trading session aktif berdasarkan UTC hour.
/// Mirror logic dari frontend SessionChip.tsx supaya bucket session konsisten antara
/// dashboard display dan trade analytics.
///
/// <para>Window (UTC hour):</para>
/// <list type="bullet">
///   <item>Sydney: 21-24 + 0-6</item>
///   <item>Tokyo: 0-9</item>
///   <item>London: 7-16</item>
///   <item>NewYork: 12-21</item>
///   <item>Overlap (London+NY): 12-16 — return "Overlap" untuk bucketing terpisah</item>
///   <item>Closed: weekend (Sat/Sun)</item>
/// </list>
/// </summary>
public static class SessionDetector
{
    public static string Detect(DateTimeOffset utcTime)
    {
        var day = utcTime.DayOfWeek;
        if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday)
            return "Closed";

        int h = utcTime.UtcDateTime.Hour;

        // Overlap London + NewYork (highest volume) — bucket terpisah karena karakteristik beda
        if (h >= 12 && h < 16) return "Overlap";
        if (h >= 7 && h < 16)  return "London";
        if (h >= 16 && h < 21) return "NewYork";
        if (h >= 21 || h < 6)  return "Sydney";
        if (h >= 0 && h < 9)   return "Tokyo";

        return "Closed";
    }
}
