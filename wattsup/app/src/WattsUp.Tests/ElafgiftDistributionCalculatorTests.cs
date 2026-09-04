using WattsUp.Services.Tariffs;

namespace WattsUp.Tests;

public class ElafgiftDistributionCalculatorTests
{
    [Fact]
    public void DistributeForDay_ConsumptionWithinAllowance_ReturnsNormalRate()
    {
        var rate = ElafgiftDistributionCalculator.DistributeForDay(
            allowanceKwh: 20m, consumptionKwh: 15m, normalRateDkkPerKwh: 0.008m, reducedRateDkkPerKwh: 0.004m);

        Assert.Equal(0.008m, rate);
    }

    [Fact]
    public void DistributeForDay_ConsumptionFullyAboveAllowance_ReturnsReducedRate()
    {
        var rate = ElafgiftDistributionCalculator.DistributeForDay(
            allowanceKwh: 0m, consumptionKwh: 10m, normalRateDkkPerKwh: 0.008m, reducedRateDkkPerKwh: 0.004m);

        Assert.Equal(0.004m, rate);
    }

    [Fact]
    public void DistributeForDay_ConsumptionPartlyAboveAllowance_ReturnsWeightedBlend()
    {
        // 5 kWh at 0.008 + 5 kWh at 0.004 over 10 kWh total -> blended 0.006.
        var rate = ElafgiftDistributionCalculator.DistributeForDay(
            allowanceKwh: 5m, consumptionKwh: 10m, normalRateDkkPerKwh: 0.008m, reducedRateDkkPerKwh: 0.004m);

        Assert.Equal(0.006m, rate);
    }

    [Fact]
    public void DistributeForDay_ZeroConsumption_ReturnsNormalRateWithoutDividingByZero()
    {
        var rate = ElafgiftDistributionCalculator.DistributeForDay(
            allowanceKwh: 5m, consumptionKwh: 0m, normalRateDkkPerKwh: 0.008m, reducedRateDkkPerKwh: 0.004m);

        Assert.Equal(0.008m, rate);
    }

    [Fact]
    public void DistributeForDay_AllowanceExceedsConsumption_ClampsToFullConsumptionAtNormalRate()
    {
        var rate = ElafgiftDistributionCalculator.DistributeForDay(
            allowanceKwh: 100m, consumptionKwh: 3m, normalRateDkkPerKwh: 0.008m, reducedRateDkkPerKwh: 0.004m);

        Assert.Equal(0.008m, rate);
    }
}
