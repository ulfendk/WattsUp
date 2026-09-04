using WattsUp.Services.Pricing;
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
}
