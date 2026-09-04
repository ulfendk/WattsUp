using Moq;
using WattsUp.Data.Repositories;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

namespace WattsUp.Tests;

public class PriceCalculationServiceTests
{
    [Fact]
    public void Calculate_WorkedExample_MatchesExpectedTotals()
    {
        // DK1, hour 14, illustrative spot/grid figures, real confirmed nationwide figures.
        var tariffs = new TariffResolution(
            GridTariffDkkPerKwh: 0.200m, GridTariffResolved: true,
            SystemTariffDkkPerKwh: 0.072m, SystemTariffResolved: true,
            TransmissionTariffDkkPerKwh: 0.043m, TransmissionTariffResolved: true,
            ElafgiftDkkPerKwh: 0.008m, ElafgiftReducedApplied: false, ElafgiftFromLiveReducedRow: false);

        var breakdown = PriceCalculationService.Calculate(
            priceArea: "DK1",
            atUtc: new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero), // 14:00 local (CET, UTC+1)
            spotPriceDkkPerKwh: 0.450m,
            spotPriceResolved: true,
            tariffs: tariffs,
            markupDkkPerKwh: 0.030m,
            vatEnabled: true);

        Assert.Equal(0.803m, breakdown.SubtotalDkkPerKwh);
        Assert.Equal(0.20075m, breakdown.VatAmountDkkPerKwh);
        Assert.Equal(1.00375m, breakdown.TotalDkkPerKwh);
        Assert.True(breakdown.FullyResolved);
    }

    [Fact]
    public void Calculate_VatDisabled_TotalEqualsSubtotal()
    {
        var tariffs = new TariffResolution(
            GridTariffDkkPerKwh: 0.200m, GridTariffResolved: true,
            SystemTariffDkkPerKwh: 0.072m, SystemTariffResolved: true,
            TransmissionTariffDkkPerKwh: 0.043m, TransmissionTariffResolved: true,
            ElafgiftDkkPerKwh: 0.008m, ElafgiftReducedApplied: false, ElafgiftFromLiveReducedRow: false);

        var breakdown = PriceCalculationService.Calculate(
            "DK1", DateTimeOffset.UtcNow, 0.450m, true, tariffs, 0.030m, vatEnabled: false);

        Assert.Equal(breakdown.SubtotalDkkPerKwh, breakdown.TotalDkkPerKwh);
        Assert.Equal(0m, breakdown.VatAmountDkkPerKwh);
    }

    [Fact]
    public void Calculate_UnresolvedGridTariff_MarksBreakdownNotFullyResolved()
    {
        var tariffs = new TariffResolution(
            GridTariffDkkPerKwh: 0m, GridTariffResolved: false,
            SystemTariffDkkPerKwh: 0.072m, SystemTariffResolved: true,
            TransmissionTariffDkkPerKwh: 0.043m, TransmissionTariffResolved: true,
            ElafgiftDkkPerKwh: 0.008m, ElafgiftReducedApplied: false, ElafgiftFromLiveReducedRow: false);

        var breakdown = PriceCalculationService.Calculate(
            "DK1", DateTimeOffset.UtcNow, 0.450m, true, tariffs, 0m, vatEnabled: true);

        Assert.False(breakdown.FullyResolved);
    }

    [Fact]
    public void Calculate_NoPricePeriodStartGiven_DefaultsToNull()
    {
        var tariffs = new TariffResolution(
            GridTariffDkkPerKwh: 0m, GridTariffResolved: true,
            SystemTariffDkkPerKwh: 0.072m, SystemTariffResolved: true,
            TransmissionTariffDkkPerKwh: 0.043m, TransmissionTariffResolved: true,
            ElafgiftDkkPerKwh: 0.008m, ElafgiftReducedApplied: false, ElafgiftFromLiveReducedRow: false);

        var breakdown = PriceCalculationService.Calculate(
            "DK1", DateTimeOffset.UtcNow, 0.450m, true, tariffs, 0m, vatEnabled: true);

        Assert.Null(breakdown.PricePeriodStartUtc);
    }

    [Fact]
    public async Task CalculateAsync_ReflectsTheSpotPricePeriodsOwnStartTime_NotJustNow()
    {
        // Denmark's day-ahead market is 15-minute resolution — "now" and the resolved spot
        // period's own start time genuinely differ, and PricePeriodStartUtc must reflect the
        // latter (what the price actually covers), not just echo back "now".
        var periodStart = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero);
        var now = periodStart.AddMinutes(7);

        var spotPriceRepository = new Mock<ISpotPriceRepository>();
        spotPriceRepository
            .Setup(r => r.GetCurrentAsync("DK1", now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpotPriceRecord("DK1", periodStart, periodStart, 0.5m));

        var tariffResolutionService = new Mock<ITariffResolutionService>();
        tariffResolutionService
            .Setup(s => s.ResolveAsync(now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TariffResolution(0m, true, 0.072m, true, 0.043m, true, 0.008m, false, false));

        var settingsRepository = new Mock<ISettingsRepository>();
        settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings());

        var sut = new PriceCalculationService(spotPriceRepository.Object, tariffResolutionService.Object, settingsRepository.Object);
        var breakdown = await sut.CalculateAsync("DK1", now);

        Assert.Equal(now, breakdown.AtUtc);
        Assert.Equal(periodStart, breakdown.PricePeriodStartUtc);
    }
}
