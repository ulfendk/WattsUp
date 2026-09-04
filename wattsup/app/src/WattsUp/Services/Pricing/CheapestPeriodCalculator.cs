namespace WattsUp.Services.Pricing;

public sealed record CheapestPeriodResult(int DurationHours, DateTimeOffset StartAtUtc, decimal AveragePriceDkkPerKwh);

/// <summary>
/// Backlog item 7: the cheapest contiguous window of 1–6 hours of continuous use, over whatever
/// price data is already available (today + published day-ahead prices) — no prediction engine
/// involved, so no confidence figure is produced either.
/// </summary>
public static class CheapestPeriodCalculator
{
    public static IReadOnlyList<CheapestPeriodResult> FindCheapestPeriods(
        IReadOnlyList<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> hourlyPrices, IReadOnlyList<int> durationsHours)
    {
        // Bucket to true clock hours first — Denmark's day-ahead market moved to 15-minute
        // settlement periods, so the raw data is no longer necessarily one point per hour. Without
        // this, "N hours" windows found almost no valid runs (adjacent 15-minute points are 15
        // minutes apart, not the 1 hour IsContiguousHourly checks for), and even a "1 hour" window
        // was really just the single cheapest quarter-hour, not an hour's average. Averaging per
        // hour first makes the rest of this resolution-agnostic: it works the same whether the
        // input is quarter-hourly, hourly, or anything else.
        var sorted = ToHourlyAverages(hourlyPrices);
        var results = new List<CheapestPeriodResult>();

        foreach (var duration in durationsHours)
        {
            var best = FindCheapestWindow(sorted, duration);
            if (best is not null)
            {
                results.Add(best);
            }
        }

        return results;
    }

    private static List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> ToHourlyAverages(
        IReadOnlyList<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> prices) =>
        prices
            .GroupBy(p => FloorToHour(p.AtUtc))
            .OrderBy(g => g.Key)
            .Select(g => (AtUtc: g.Key, TotalDkkPerKwh: g.Average(p => p.TotalDkkPerKwh)))
            .ToList();

    private static DateTimeOffset FloorToHour(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset);

    private static CheapestPeriodResult? FindCheapestWindow(
        List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> sorted, int durationHours)
    {
        if (durationHours <= 0 || sorted.Count < durationHours)
        {
            return null;
        }

        decimal? bestSum = null;
        var bestStartIndex = -1;

        for (var start = 0; start <= sorted.Count - durationHours; start++)
        {
            if (!IsContiguousHourly(sorted, start, durationHours))
            {
                continue;
            }

            var sum = 0m;
            for (var offset = 0; offset < durationHours; offset++)
            {
                sum += sorted[start + offset].TotalDkkPerKwh;
            }

            if (bestSum is null || sum < bestSum.Value)
            {
                bestSum = sum;
                bestStartIndex = start;
            }
        }

        return bestSum is null
            ? null
            : new CheapestPeriodResult(durationHours, sorted[bestStartIndex].AtUtc, bestSum.Value / durationHours);
    }

    private static bool IsContiguousHourly(List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> sorted, int start, int count)
    {
        for (var i = start + 1; i < start + count; i++)
        {
            if (sorted[i].AtUtc - sorted[i - 1].AtUtc != TimeSpan.FromHours(1))
            {
                return false;
            }
        }
        return true;
    }
}
