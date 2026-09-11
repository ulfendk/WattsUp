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
        var sorted = hourlyPrices.OrderBy(p => p.AtUtc).ToList();

        // Slide at whatever resolution the data actually is — Denmark's day-ahead market moved to
        // 15-minute settlement periods, so a window doesn't have to start on the hour any more; a
        // window starting at e.g. 11:15 can be cheaper than either 11:00 or 12:00. Detected rather
        // than hardcoded so this keeps working if the resolution changes again.
        var interval = DetectInterval(sorted);
        var results = new List<CheapestPeriodResult>();

        foreach (var duration in durationsHours)
        {
            var windowPoints = interval > TimeSpan.Zero
                ? (int)Math.Round(TimeSpan.FromHours(duration) / interval)
                : 0;
            var best = FindCheapestWindow(sorted, windowPoints, interval, duration);
            if (best is not null)
            {
                results.Add(best);
            }
        }

        return results;
    }

    /// <summary>The smallest gap between consecutive points — robust against an occasional larger
    /// gap (e.g. one missing sample) skewing the detected resolution.</summary>
    private static TimeSpan DetectInterval(List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> sorted)
    {
        var minGap = TimeSpan.Zero;
        for (var i = 1; i < sorted.Count; i++)
        {
            var gap = sorted[i].AtUtc - sorted[i - 1].AtUtc;
            if (gap > TimeSpan.Zero && (minGap == TimeSpan.Zero || gap < minGap))
            {
                minGap = gap;
            }
        }
        return minGap == TimeSpan.Zero ? TimeSpan.FromHours(1) : minGap;
    }

    private static CheapestPeriodResult? FindCheapestWindow(
        List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> sorted, int windowPoints, TimeSpan interval, int durationHours)
    {
        if (windowPoints <= 0 || sorted.Count < windowPoints)
        {
            return null;
        }

        decimal? bestSum = null;
        var bestStartIndex = -1;

        for (var start = 0; start <= sorted.Count - windowPoints; start++)
        {
            if (!IsContiguous(sorted, start, windowPoints, interval))
            {
                continue;
            }

            var sum = 0m;
            for (var offset = 0; offset < windowPoints; offset++)
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
            : new CheapestPeriodResult(durationHours, sorted[bestStartIndex].AtUtc, bestSum.Value / windowPoints);
    }

    private static bool IsContiguous(
        List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> sorted, int start, int count, TimeSpan interval)
    {
        for (var i = start + 1; i < start + count; i++)
        {
            if (sorted[i].AtUtc - sorted[i - 1].AtUtc != interval)
            {
                return false;
            }
        }
        return true;
    }
}
