using WattsUp.Services.Pricing;

namespace WattsUp.Tests;

public class CheapestPeriodCalculatorTests
{
    private static List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)> HourlyPrices(params decimal[] pricesStartingAtMidnight)
    {
        var start = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        return pricesStartingAtMidnight
            .Select((price, hour) => (start.AddHours(hour), price))
            .ToList();
    }

    [Fact]
    public void FindCheapestPeriods_OneHourDuration_PicksTheSingleCheapestHour()
    {
        var prices = HourlyPrices(1.0m, 0.5m, 2.0m, 0.3m, 1.5m);

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [1]);

        Assert.Single(results);
        Assert.Equal(prices[3].AtUtc, results[0].StartAtUtc); // hour index 3 = 0.3
        Assert.Equal(0.3m, results[0].AveragePriceDkkPerKwh);
    }

    [Fact]
    public void FindCheapestPeriods_MultiHourDuration_PicksCheapestContiguousWindow()
    {
        // Cheapest 2-hour window is hours 3-4 (0.1 + 0.1 = 0.2), not hours 1-2 (0.2 + 0.9 = 1.1).
        var prices = HourlyPrices(1.0m, 0.2m, 0.9m, 0.1m, 0.1m, 1.0m);

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [2]);

        Assert.Single(results);
        Assert.Equal(prices[3].AtUtc, results[0].StartAtUtc);
        Assert.Equal(0.1m, results[0].AveragePriceDkkPerKwh);
    }

    [Fact]
    public void FindCheapestPeriods_NotEnoughDataForDuration_OmitsThatDuration()
    {
        var prices = HourlyPrices(1.0m, 0.5m);

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [1, 6]);

        Assert.Single(results);
        Assert.Equal(1, results[0].DurationHours);
    }

    [Fact]
    public void FindCheapestPeriods_GapInHourlyData_SkipsWindowsThatSpanTheGap()
    {
        var start = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var prices = new List<(DateTimeOffset, decimal)>
        {
            (start, 0.1m),
            (start.AddHours(1), 0.1m),
            // gap: hour 2 missing
            (start.AddHours(3), 0.05m),
            (start.AddHours(4), 0.05m),
        };

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [3]);

        Assert.Empty(results); // no 3 contiguous hours exist anywhere in the data
    }

    [Fact]
    public void FindCheapestPeriods_ReturnsAllRequestedDurationsIndependently()
    {
        var prices = HourlyPrices(0.5m, 0.5m, 0.1m, 0.1m, 0.1m, 0.5m);

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [1, 2, 3]);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(0.1m, r.AveragePriceDkkPerKwh));
    }

    // Denmark's day-ahead market moved to 15-minute settlement periods — regression coverage for
    // that: multi-hour windows must still be found, and each hour's price must be the average of
    // its four quarter-hour prices, not just one of them.
    [Fact]
    public void FindCheapestPeriods_QuarterHourlyResolution_AveragesToHoursBeforeSearching()
    {
        var start = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var prices = new List<(DateTimeOffset, decimal)>();
        // Hour 0: avg 1.0 (expensive). Hour 1: avg 0.2 (cheap). Hour 2: avg 0.2 (cheap).
        foreach (var (hour, quarterPrices) in new[]
        {
            (0, new[] { 0.9m, 1.0m, 1.0m, 1.1m }),
            (1, new[] { 0.1m, 0.2m, 0.2m, 0.3m }),
            (2, new[] { 0.15m, 0.2m, 0.2m, 0.25m }),
        })
        {
            for (var q = 0; q < 4; q++)
            {
                prices.Add((start.AddHours(hour).AddMinutes(q * 15), quarterPrices[q]));
            }
        }

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [1, 2]);

        Assert.Equal(2, results.Count);
        var oneHour = results.Single(r => r.DurationHours == 1);
        Assert.Equal(start.AddHours(1), oneHour.StartAtUtc);
        Assert.Equal(0.2m, oneHour.AveragePriceDkkPerKwh);

        var twoHour = results.Single(r => r.DurationHours == 2);
        Assert.Equal(start.AddHours(1), twoHour.StartAtUtc);
        Assert.Equal(0.2m, twoHour.AveragePriceDkkPerKwh);
    }

    [Fact]
    public void FindCheapestPeriods_QuarterHourlyResolution_GapWithinAnHourStillCountsAsThatHour()
    {
        // A single missing quarter-hour shouldn't create a false "gap" at the hourly level once
        // bucketed — only a genuinely missing hour (backlog-relevant when a poll hasn't run yet)
        // should break contiguity.
        var start = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var prices = new List<(DateTimeOffset, decimal)>
        {
            (start, 0.5m),
            // start.AddMinutes(15) missing
            (start.AddMinutes(30), 0.5m),
            (start.AddMinutes(45), 0.5m),
            (start.AddHours(1), 0.1m),
        };

        var results = CheapestPeriodCalculator.FindCheapestPeriods(prices, [2]);

        Assert.Single(results);
        Assert.Equal(start, results[0].StartAtUtc);
    }
}
