namespace WattsUp.Services.Tariffs;

/// <summary>
/// Backlog item 13: once a day is over, don't apply the electric-heating elafgift threshold as a
/// hard cutover mid-day (which sub-daily hourly consumption isn't actually known well enough to
/// place precisely) — instead blend that day's normal/reduced split into one effective DKK/kWh rate,
/// weighted by how much of the day's consumption fell within vs. above the day's allowance, and
/// apply that single blended rate uniformly across the day's 24 hours.
/// </summary>
public static class ElafgiftDistributionCalculator
{
    public static decimal DistributeForDay(
        decimal allowanceKwh, decimal consumptionKwh, decimal normalRateDkkPerKwh, decimal reducedRateDkkPerKwh)
    {
        if (consumptionKwh <= 0m)
        {
            return normalRateDkkPerKwh;
        }

        var normalKwh = Math.Clamp(allowanceKwh, 0m, consumptionKwh);
        var reducedKwh = consumptionKwh - normalKwh;

        var totalElafgift = normalKwh * normalRateDkkPerKwh + reducedKwh * reducedRateDkkPerKwh;
        return totalElafgift / consumptionKwh;
    }
}
