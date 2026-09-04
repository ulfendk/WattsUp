using WattsUp.Data.Repositories;
using WattsUp.Services.Tariffs;

namespace WattsUp.Services.Pricing;

public sealed record HourOverHourTrend(decimal CurrentTotalDkkPerKwh, decimal PreviousHourTotalDkkPerKwh, decimal DeltaPercent);

public sealed record TodayPriceRange(decimal MinDkkPerKwh, decimal MaxDkkPerKwh, decimal CurrentDkkPerKwh);

public sealed record SameHourComparison(decimal TodayDkkPerKwh, decimal AverageOverPastDaysDkkPerKwh, int DaysCompared);

public sealed record AverageComparison(decimal TodayAverageDkkPerKwh, decimal AverageOverPastDaysDkkPerKwh, int DaysCompared);

/// <summary>
/// Backlog item 9: trend/comparison figures for the Dashboard, all derived from already-cached spot
/// prices (<see cref="ISpotPriceRepository"/>) run through the same <see cref="IPriceCalculationService"/>
/// the chart and price card use — "price" here always means the full incl.-markup/incl.-tariffs total,
/// not the bare spot price, since that's what a household actually pays.
/// </summary>
public interface IPriceTrendService
{
    Task<HourOverHourTrend?> GetHourOverHourTrendAsync(string priceArea, DateTimeOffset atUtc, CancellationToken ct = default);

    Task<TodayPriceRange?> GetTodayRangeAsync(string priceArea, DateTimeOffset atUtc, CancellationToken ct = default);

    Task<SameHourComparison?> GetSameHourLastNDaysAsync(
        string priceArea, DateTimeOffset atUtc, int days = 7, CancellationToken ct = default);

    Task<AverageComparison?> GetTodayVsLastNDaysAverageAsync(
        string priceArea, DateTimeOffset atUtc, int days = 7, CancellationToken ct = default);
}

public sealed class PriceTrendService(ISpotPriceRepository spotPriceRepository, IPriceCalculationService priceCalculationService)
    : IPriceTrendService
{
    public async Task<HourOverHourTrend?> GetHourOverHourTrendAsync(string priceArea, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        var currentHour = FloorToHour(atUtc);
        var previousHour = currentHour.AddHours(-1);

        var currentSpot = await spotPriceRepository.GetCurrentAsync(priceArea, currentHour, ct);
        var previousSpot = await spotPriceRepository.GetCurrentAsync(priceArea, previousHour, ct);
        if (currentSpot is null || previousSpot is null || currentSpot.TimeUtc != currentHour || previousSpot.TimeUtc != previousHour)
        {
            return null;
        }

        var current = await priceCalculationService.CalculateAsync(priceArea, currentHour, ct);
        var previous = await priceCalculationService.CalculateAsync(priceArea, previousHour, ct);
        if (previous.TotalDkkPerKwh == 0m)
        {
            return null;
        }

        var deltaPercent = (current.TotalDkkPerKwh - previous.TotalDkkPerKwh) / Math.Abs(previous.TotalDkkPerKwh) * 100m;
        return new HourOverHourTrend(current.TotalDkkPerKwh, previous.TotalDkkPerKwh, deltaPercent);
    }

    public async Task<TodayPriceRange?> GetTodayRangeAsync(string priceArea, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = LocalDayRange(atUtc);
        var totals = await GetHourlyTotalsAsync(priceArea, fromUtc, toUtc, ct);
        if (totals.Count == 0)
        {
            return null;
        }

        var currentHour = FloorToHour(atUtc);
        // No cached price for the exact current hour (e.g. mid-poll)? Use the closest known hour instead.
        var current = totals.Any(t => t.AtUtc == currentHour)
            ? totals.First(t => t.AtUtc == currentHour).TotalDkkPerKwh
            : totals.OrderBy(t => Math.Abs((t.AtUtc - currentHour).Ticks)).First().TotalDkkPerKwh;

        return new TodayPriceRange(totals.Min(t => t.TotalDkkPerKwh), totals.Max(t => t.TotalDkkPerKwh), current);
    }

    public async Task<SameHourComparison?> GetSameHourLastNDaysAsync(
        string priceArea, DateTimeOffset atUtc, int days = 7, CancellationToken ct = default)
    {
        var currentHour = FloorToHour(atUtc);
        var currentSpot = await spotPriceRepository.GetCurrentAsync(priceArea, currentHour, ct);
        if (currentSpot is null || currentSpot.TimeUtc != currentHour)
        {
            return null;
        }
        var todayPrice = (await priceCalculationService.CalculateAsync(priceArea, currentHour, ct)).TotalDkkPerKwh;

        var pastPrices = new List<decimal>();
        for (var i = 1; i <= days; i++)
        {
            var pastHour = currentHour.AddDays(-i);
            var pastSpot = await spotPriceRepository.GetCurrentAsync(priceArea, pastHour, ct);
            if (pastSpot is null || pastSpot.TimeUtc != pastHour)
            {
                continue;
            }
            pastPrices.Add((await priceCalculationService.CalculateAsync(priceArea, pastHour, ct)).TotalDkkPerKwh);
        }

        return pastPrices.Count == 0 ? null : new SameHourComparison(todayPrice, pastPrices.Average(), pastPrices.Count);
    }

    public async Task<AverageComparison?> GetTodayVsLastNDaysAverageAsync(
        string priceArea, DateTimeOffset atUtc, int days = 7, CancellationToken ct = default)
    {
        var (todayFromUtc, todayToUtc) = LocalDayRange(atUtc);
        var todayTotals = await GetHourlyTotalsAsync(priceArea, todayFromUtc, todayToUtc, ct);
        if (todayTotals.Count == 0)
        {
            return null;
        }
        var todayAverage = todayTotals.Average(t => t.TotalDkkPerKwh);

        var dailyAverages = new List<decimal>();
        for (var i = 1; i <= days; i++)
        {
            var (pastFromUtc, pastToUtc) = LocalDayRange(atUtc.AddDays(-i));
            var pastTotals = await GetHourlyTotalsAsync(priceArea, pastFromUtc, pastToUtc, ct);
            if (pastTotals.Count > 0)
            {
                dailyAverages.Add(pastTotals.Average(t => t.TotalDkkPerKwh));
            }
        }

        return dailyAverages.Count == 0 ? null : new AverageComparison(todayAverage, dailyAverages.Average(), dailyAverages.Count);
    }

    private async Task<List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)>> GetHourlyTotalsAsync(
        string priceArea, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var spotPrices = await spotPriceRepository.GetRangeAsync(priceArea, fromUtc, toUtc, ct);
        var totals = new List<(DateTimeOffset, decimal)>(spotPrices.Count);
        foreach (var spot in spotPrices)
        {
            var breakdown = await priceCalculationService.CalculateAsync(priceArea, spot.TimeUtc, ct);
            totals.Add((spot.TimeUtc, breakdown.TotalDkkPerKwh));
        }
        return totals;
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) LocalDayRange(DateTimeOffset atUtc)
    {
        var local = TimeZoneInfo.ConvertTime(atUtc, TariffResolutionService.CopenhagenTimeZone);
        var localMidnight = new DateTimeOffset(local.Date, local.Offset);
        var fromUtc = localMidnight.ToUniversalTime();
        return (fromUtc, fromUtc.AddDays(1));
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset atUtc) =>
        new(atUtc.Year, atUtc.Month, atUtc.Day, atUtc.Hour, 0, 0, atUtc.Offset);
}
