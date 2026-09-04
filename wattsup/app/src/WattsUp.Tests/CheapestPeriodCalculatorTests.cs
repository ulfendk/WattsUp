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
}
