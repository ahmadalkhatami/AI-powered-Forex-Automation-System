namespace ForexAI.Infrastructure.Broker.Oanda;

internal static class ForexIndicators
{
    internal static decimal Sma(decimal[] prices, int period)
    {
        if (prices.Length < period) return prices.Average();
        return prices[^period..].Average();
    }

    // Wilder's smoothed RSI
    internal static decimal Rsi(decimal[] closes, int period = 14)
    {
        if (closes.Length < period + 1) return 50m;

        var changes = new decimal[closes.Length - 1];
        for (int i = 0; i < changes.Length; i++)
            changes[i] = closes[i + 1] - closes[i];

        decimal avgGain = 0m, avgLoss = 0m;
        for (int i = 0; i < period; i++)
        {
            if (changes[i] > 0) avgGain += changes[i];
            else avgLoss += Math.Abs(changes[i]);
        }
        avgGain /= period;
        avgLoss /= period;

        for (int i = period; i < changes.Length; i++)
        {
            decimal gain = changes[i] > 0 ? changes[i] : 0m;
            decimal loss = changes[i] < 0 ? Math.Abs(changes[i]) : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0m) return 100m;
        decimal rs = avgGain / avgLoss;
        return Math.Round(100m - (100m / (1m + rs)), 2);
    }

    internal static string DetectSession(DateTimeOffset time)
    {
        int hour = time.UtcDateTime.Hour;
        bool london = hour >= 7 && hour < 16;
        bool newYork = hour >= 12 && hour < 21;
        bool tokyo = hour >= 0 && hour < 9;

        return (london, newYork) switch
        {
            (true, true) => "London/New York",
            (true, false) => "London",
            (false, true) => "New York",
            _ => tokyo ? "Tokyo" : "Sydney/Pacific"
        };
    }

    internal static (string Support, string Resistance) DetectZones(
        decimal[] highs, decimal[] lows, int lookback = 20)
    {
        int take = Math.Min(lookback, Math.Min(highs.Length, lows.Length));
        decimal support = lows[^take..].Min();
        decimal resistance = highs[^take..].Max();

        const decimal halfZone = 0.00050m;
        string supportZone = $"{support - halfZone:F4}–{support + halfZone:F4}";
        string resistanceZone = $"{resistance - halfZone:F4}–{resistance + halfZone:F4}";
        return (supportZone, resistanceZone);
    }
}
