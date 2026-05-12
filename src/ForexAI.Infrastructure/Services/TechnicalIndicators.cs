namespace ForexAI.Infrastructure.Services;

public static class TechnicalIndicators
{
    public static decimal CalculateSMA(decimal[] prices, int period)
    {
        if (prices.Length < period) return 0;
        return prices.TakeLast(period).Average();
    }

    public static (decimal Rsi, string Direction) CalculateRSI(decimal[] closePrices, int period = 14)
    {
        if (closePrices.Length <= period) return (50m, "Netral");

        decimal gains = 0;
        decimal losses = 0;

        for (int i = 1; i <= period; i++)
        {
            decimal diff = closePrices[i] - closePrices[i - 1];
            if (diff > 0) gains += diff;
            else losses -= diff;
        }

        decimal avgGain = gains / period;
        decimal avgLoss = losses / period;

        for (int i = period + 1; i < closePrices.Length; i++)
        {
            decimal diff = closePrices[i] - closePrices[i - 1];
            decimal gain = diff > 0 ? diff : 0;
            decimal loss = diff < 0 ? -diff : 0;

            avgGain = ((avgGain * (period - 1)) + gain) / period;
            avgLoss = ((avgLoss * (period - 1)) + loss) / period;
        }

        if (avgLoss == 0) return (100m, "Naik");
        
        decimal rs = avgGain / avgLoss;
        decimal rsi = 100m - (100m / (1m + rs));

        // Determine direction by comparing last few RSI values (simplified)
        string direction = "Netral";
        if (closePrices.Length > period + 2)
        {
            // Just basic heuristic for direction
            decimal prevDiff = closePrices[^1] - closePrices[^2];
            direction = prevDiff > 0 ? "Naik" : (prevDiff < 0 ? "Turun" : "Netral");
        }

        return (rsi, direction);
    }

    public static (string Support, string Resistance) FindZones(decimal[] lows, decimal[] highs)
    {
        if (lows.Length == 0 || highs.Length == 0) return ("-", "-");
        
        // Simplified approach: use recent lowest low and highest high as zones
        decimal support = lows.Min();
        decimal resistance = highs.Max();
        
        return (support.ToString("0.0000"), resistance.ToString("0.0000"));
    }
}
