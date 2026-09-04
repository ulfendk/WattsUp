using Moq;
using WattsUp.Data.Repositories;
using WattsUp.Services.Pricing;

namespace WattsUp.Tests;

public class PriceTrendServiceTests
{
    private readonly Mock<ISpotPriceRepository> _spotPriceRepository = new();
    private readonly Mock<IPriceCalculationService> _priceCalculationService = new();

    private PriceTrendService CreateSut() => new(_spotPriceRepository.Object, _priceCalculationService.Object);

    private static PriceBreakdown Breakdown(DateTimeOffset atUtc, decimal totalDkkPerKwh) => new()
    {
        PriceArea = "DK1",
        AtUtc = atUtc,
        SpotPriceDkkPerKwh = 0m,
        SpotPriceResolved = true,
        GridTariffDkkPerKwh = 0m,
        GridTariffResolved = true,
        SystemTariffDkkPerKwh = 0m,
        TransmissionTariffDkkPerKwh = 0m,
        NationwideChargesResolved = true,
        ElafgiftDkkPerKwh = 0m,
        ElafgiftReducedApplied = false,
        MarkupDkkPerKwh = 0m,
        SubtotalDkkPerKwh = totalDkkPerKwh,
        VatEnabled = false,
        VatAmountDkkPerKwh = 0m,
        TotalDkkPerKwh = totalDkkPerKwh,
    };

    private static SpotPriceRecord Spot(DateTimeOffset atUtc, decimal price) => new("DK1", atUtc, atUtc, price);

    [Fact]
    public async Task GetHourOverHourTrendAsync_PriceRoseTenPercent_ReturnsPositiveDeltaPercent()
    {
        var currentHour = new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero);
        var previousHour = currentHour.AddHours(-1);

        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", currentHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Spot(currentHour, 1.1m));
        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", previousHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Spot(previousHour, 1.0m));
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", currentHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Breakdown(currentHour, 1.10m));
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", previousHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Breakdown(previousHour, 1.00m));

        var sut = CreateSut();
        var trend = await sut.GetHourOverHourTrendAsync("DK1", currentHour);

        Assert.NotNull(trend);
        Assert.Equal(10m, trend!.DeltaPercent);
    }

    [Fact]
    public async Task GetHourOverHourTrendAsync_NoPreviousHourData_ReturnsNull()
    {
        var currentHour = new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero);
        var previousHour = currentHour.AddHours(-1);
        var staleHour = previousHour.AddHours(-5);

        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", currentHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Spot(currentHour, 1.1m));
        // GetCurrentAsync falls back to the most recent earlier record when there's a gap — simulate that.
        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", previousHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Spot(staleHour, 0.9m));

        var sut = CreateSut();
        var trend = await sut.GetHourOverHourTrendAsync("DK1", currentHour);

        Assert.Null(trend);
    }

    [Fact]
    public async Task GetTodayRangeAsync_ReturnsMinMaxAndCurrent()
    {
        var now = new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero);
        var hour0 = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var hours = Enumerable.Range(0, 24).Select(h => hour0.AddHours(h)).ToList();
        _spotPriceRepository
            .Setup(r => r.GetRangeAsync("DK1", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hours.Select(h => Spot(h, 1.0m)).ToList());

        foreach (var h in hours)
        {
            var price = h.Hour switch { 3 => 0.2m, 18 => 2.5m, 13 => 1.3m, _ => 1.0m };
            _priceCalculationService.Setup(s => s.CalculateAsync("DK1", h, It.IsAny<CancellationToken>())).ReturnsAsync(Breakdown(h, price));
        }

        var sut = CreateSut();
        var range = await sut.GetTodayRangeAsync("DK1", now);

        Assert.NotNull(range);
        Assert.Equal(0.2m, range!.MinDkkPerKwh);
        Assert.Equal(2.5m, range.MaxDkkPerKwh);
        Assert.Equal(1.3m, range.CurrentDkkPerKwh);
    }

    [Fact]
    public async Task GetTodayRangeAsync_NoCachedData_ReturnsNull()
    {
        _spotPriceRepository
            .Setup(r => r.GetRangeAsync("DK1", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var range = await sut.GetTodayRangeAsync("DK1", DateTimeOffset.UtcNow);

        Assert.Null(range);
    }

    [Fact]
    public async Task GetSameHourLastNDaysAsync_AveragesOnlyDaysWithData()
    {
        var currentHour = new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero);

        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", currentHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Spot(currentHour, 1.5m));
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", currentHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Breakdown(currentHour, 1.5m));

        // Only 2 of the past 7 days have cached data, at prices 1.0 and 2.0 -> average 1.5.
        var day1 = currentHour.AddDays(-1);
        var day3 = currentHour.AddDays(-3);
        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", day1, It.IsAny<CancellationToken>())).ReturnsAsync(Spot(day1, 1.0m));
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", day1, It.IsAny<CancellationToken>())).ReturnsAsync(Breakdown(day1, 1.0m));
        _spotPriceRepository.Setup(r => r.GetCurrentAsync("DK1", day3, It.IsAny<CancellationToken>())).ReturnsAsync(Spot(day3, 2.0m));
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", day3, It.IsAny<CancellationToken>())).ReturnsAsync(Breakdown(day3, 2.0m));

        var sut = CreateSut();
        var comparison = await sut.GetSameHourLastNDaysAsync("DK1", currentHour, days: 7);

        Assert.NotNull(comparison);
        Assert.Equal(1.5m, comparison!.TodayDkkPerKwh);
        Assert.Equal(1.5m, comparison.AverageOverPastDaysDkkPerKwh);
        Assert.Equal(2, comparison.DaysCompared);
    }
}
