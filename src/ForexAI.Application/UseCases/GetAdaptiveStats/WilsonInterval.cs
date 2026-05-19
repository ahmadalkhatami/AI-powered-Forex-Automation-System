namespace ForexAI.Application.UseCases.GetAdaptiveStats;

/// <summary>
/// Wilson score confidence interval — lebih akurat daripada normal approximation
/// untuk small sample dan extreme proportions (WR near 0 or 1).
///
/// <para>Formula (95% confidence, z = 1.96):</para>
/// <code>
/// center = (p + z² / 2n) / (1 + z² / n)
/// width  = z × √(p(1-p)/n + z²/(4n²)) / (1 + z²/n)
/// </code>
/// </summary>
public static class WilsonInterval
{
    private const double Z95 = 1.96;

    public static (decimal lower, decimal upper) Compute(int wins, int trials)
    {
        if (trials <= 0) return (0m, 0m);
        if (wins < 0) wins = 0;
        if (wins > trials) wins = trials;

        double n = trials;
        double p = (double)wins / n;
        double z2 = Z95 * Z95;
        double denom = 1.0 + z2 / n;
        double center = (p + z2 / (2.0 * n)) / denom;
        double margin = Z95 * Math.Sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / denom;

        double lower = Math.Max(0.0, center - margin);
        double upper = Math.Min(1.0, center + margin);
        return ((decimal)Math.Round(lower, 4), (decimal)Math.Round(upper, 4));
    }
}
