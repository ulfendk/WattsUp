using Moq;
using WattsUp.Data.Repositories;
using WattsUp.Services.Consumption;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

namespace WattsUp.Tests;

public class DeviceCostServiceTests
{
    private readonly Mock<IDeviceHourlyConsumptionRepository> _consumptionRepository = new();
    private readonly Mock<IPriceCalculationService> _priceCalculationService = new();
    private readonly Mock<ISettingsRepository> _settingsRepository = new();

    private DeviceCostService CreateSut() => new(
        _consumptionRepository.Object, _priceCalculationService.Object, _settingsRepository.Object);

    private static PriceBreakdown Breakdown(decimal totalDkkPerKwh) => new()
    {
        PriceArea = "DK1",
        AtUtc = DateTimeOffset.UtcNow,
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

    [Fact]
    public async Task GetHourlyCostsAsync_MultipliesEachHoursKwhByThatHoursPrice()
    {
        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings { PriceArea = "DK1" });

        var hour1 = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var hour2 = hour1.AddHours(1);
        _consumptionRepository
            .Setup(r => r.GetRangeAsync("sensor.ev_charger", hour1, hour2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new DeviceHourlyConsumption("sensor.ev_charger", hour1, 2.0m),
                new DeviceHourlyConsumption("sensor.ev_charger", hour2, 1.5m),
            ]);
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", hour1, It.IsAny<CancellationToken>())).ReturnsAsync(Breakdown(1.00m));
        _priceCalculationService.Setup(s => s.CalculateAsync("DK1", hour2, It.IsAny<CancellationToken>())).ReturnsAsync(Breakdown(2.00m));

        var sut = CreateSut();
        var costs = await sut.GetHourlyCostsAsync("sensor.ev_charger", hour1, hour2);

        Assert.Equal(2, costs.Count);
        Assert.Equal(2.0m, costs[0].CostDkk); // 2.0 kWh * 1.00 DKK/kWh
        Assert.Equal(3.0m, costs[1].CostDkk); // 1.5 kWh * 2.00 DKK/kWh
    }

    [Fact]
    public async Task GetCurrentHourCostAsync_LatestReadingOlderThanCurrentHour_ReturnsNull()
    {
        _consumptionRepository
            .Setup(r => r.GetLatestAsync("sensor.ev_charger", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceHourlyConsumption("sensor.ev_charger", DateTimeOffset.UtcNow.AddHours(-3), 1.0m));

        var sut = CreateSut();
        var cost = await sut.GetCurrentHourCostAsync("sensor.ev_charger");

        Assert.Null(cost);
    }

    [Fact]
    public async Task GetCurrentHourCostAsync_LatestReadingWithinCurrentHour_ReturnsCost()
    {
        var now = DateTimeOffset.UtcNow;
        var hourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings { PriceArea = "DK2" });
        _consumptionRepository
            .Setup(r => r.GetLatestAsync("sensor.ev_charger", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceHourlyConsumption("sensor.ev_charger", hourStart, 0.5m));
        _priceCalculationService
            .Setup(s => s.CalculateAsync("DK2", hourStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Breakdown(4.0m));

        var sut = CreateSut();
        var cost = await sut.GetCurrentHourCostAsync("sensor.ev_charger");

        Assert.NotNull(cost);
        Assert.Equal(2.0m, cost!.CostDkk); // 0.5 kWh * 4.0 DKK/kWh
    }
}
