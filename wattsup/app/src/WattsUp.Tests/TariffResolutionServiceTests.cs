using Moq;
using WattsUp.Data.Repositories;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

namespace WattsUp.Tests;

public class TariffResolutionServiceTests
{
    private readonly Mock<ITariffRepository> _tariffRepository = new();
    private readonly Mock<INationwideChargeSeedRepository> _seedRepository = new();
    private readonly Mock<ISettingsRepository> _settingsRepository = new();
    private readonly Mock<IConsumptionRepository> _consumptionRepository = new();

    private static readonly IReadOnlyList<NationwideChargeSeed> Seeds =
    [
        new("system_tariff", NationwideCharges.SystemOperatorGln, NationwideCharges.SystemTariffChargeTypeCode, "Systemtarif", NationwideCharges.SystemTariffDkkPerKwh),
        new("transmission_tariff", NationwideCharges.SystemOperatorGln, NationwideCharges.TransmissionTariffChargeTypeCode, "Transmissions nettarif", NationwideCharges.TransmissionTariffDkkPerKwh),
        new("elafgift", NationwideCharges.SystemOperatorGln, NationwideCharges.ElafgiftChargeTypeCode, "Elafgift", NationwideCharges.NormalElafgiftDkkPerKwh),
    ];

    private TariffResolutionService CreateSut() => new(
        _tariffRepository.Object, _seedRepository.Object, _settingsRepository.Object, _consumptionRepository.Object);

    private static TariffLineItem FlatRow(string gln, string code, decimal rate) => new()
    {
        GlnNumber = gln,
        ChargeTypeCode = code,
        ChargeOwner = "Test",
        ValidFrom = new DateOnly(2026, 1, 1),
        ValidTo = null,
        ResolutionDuration = "P1D",
        Prices = [rate],
        ChargeClassification = ChargeClassification.PerKwh,
    };

    [Fact]
    public async Task ResolveAsync_SumsPerKwhGridRowsForSelectedHour()
    {
        var settings = new AppSettings { GridCompanyGln = "1234567890123" };
        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _seedRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Seeds);

        var gridRow = new TariffLineItem
        {
            GlnNumber = "1234567890123",
            ChargeTypeCode = "40000",
            ChargeOwner = "Test Grid Co",
            ValidFrom = new DateOnly(2026, 1, 1),
            ResolutionDuration = "PT1H",
            Prices = Enumerable.Range(0, 24).Select(h => (decimal)h / 100).ToList(), // hour 14 -> 0.14
            ChargeClassification = ChargeClassification.PerKwh,
        };
        _tariffRepository.Setup(r => r.GetPerKwhRowsAsync("1234567890123", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([gridRow]);

        _tariffRepository
            .Setup(r => r.GetByChargeTypeCodeAsync(NationwideCharges.SystemOperatorGln, It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string gln, string code, DateOnly date, CancellationToken _) =>
                FlatRow(gln, code, code switch
                {
                    NationwideCharges.SystemTariffChargeTypeCode => NationwideCharges.SystemTariffDkkPerKwh,
                    NationwideCharges.TransmissionTariffChargeTypeCode => NationwideCharges.TransmissionTariffDkkPerKwh,
                    _ => NationwideCharges.NormalElafgiftDkkPerKwh,
                }));

        var sut = CreateSut();
        // 13:00 UTC = 14:00 CET in January (UTC+1).
        var result = await sut.ResolveAsync(new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero));

        Assert.True(result.GridTariffResolved);
        Assert.Equal(0.14m, result.GridTariffDkkPerKwh);
        Assert.Equal(NationwideCharges.SystemTariffDkkPerKwh, result.SystemTariffDkkPerKwh);
        Assert.Equal(NationwideCharges.TransmissionTariffDkkPerKwh, result.TransmissionTariffDkkPerKwh);
        Assert.Equal(NationwideCharges.NormalElafgiftDkkPerKwh, result.ElafgiftDkkPerKwh);
        Assert.False(result.ElafgiftReducedApplied);
    }

    [Fact]
    public async Task ResolveAsync_NoGridCompanySelected_GridTariffUnresolved()
    {
        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings());
        _seedRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Seeds);
        _tariffRepository
            .Setup(r => r.GetByChargeTypeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TariffLineItem?)null);

        var sut = CreateSut();
        var result = await sut.ResolveAsync(DateTimeOffset.UtcNow);

        Assert.False(result.GridTariffResolved);
        Assert.Equal(0m, result.GridTariffDkkPerKwh);
        // Nationwide charges fall back to the seed's cached rate when no live row resolves.
        Assert.Equal(NationwideCharges.SystemTariffDkkPerKwh, result.SystemTariffDkkPerKwh);
        Assert.False(result.SystemTariffResolved);
    }

    [Fact]
    public async Task ResolveAsync_ElectricHeatingAboveThreshold_UsesReducedFallbackRate()
    {
        var settings = new AppSettings
        {
            ElectricHeatingRegistered = true,
            SelectedMeteringPointGsrn = "571234567890123456",
            ReducedElafgiftRateDkkPerKwh = 0.005m,
        };
        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _seedRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Seeds);
        _consumptionRepository
            .Setup(r => r.GetYearToDateKwhAsync("571234567890123456", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5000m);
        _tariffRepository
            .Setup(r => r.GetByChargeTypeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FlatRow(NationwideCharges.SystemOperatorGln, NationwideCharges.ElafgiftChargeTypeCode, NationwideCharges.NormalElafgiftDkkPerKwh));
        _tariffRepository
            .Setup(r => r.GetAllRowsAsync(NationwideCharges.SystemOperatorGln, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]); // no distinct "reduceret" row published yet

        var sut = CreateSut();
        var result = await sut.ResolveAsync(DateTimeOffset.UtcNow);

        Assert.True(result.ElafgiftReducedApplied);
        Assert.Equal(0.005m, result.ElafgiftDkkPerKwh);
    }

    [Fact]
    public async Task ResolveAsync_ElectricHeatingBelowThreshold_UsesNormalRate()
    {
        var settings = new AppSettings
        {
            ElectricHeatingRegistered = true,
            SelectedMeteringPointGsrn = "571234567890123456",
            ReducedElafgiftRateDkkPerKwh = 0.005m,
        };
        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _seedRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Seeds);
        _consumptionRepository
            .Setup(r => r.GetYearToDateKwhAsync("571234567890123456", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3000m);
        _tariffRepository
            .Setup(r => r.GetByChargeTypeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FlatRow(NationwideCharges.SystemOperatorGln, NationwideCharges.ElafgiftChargeTypeCode, NationwideCharges.NormalElafgiftDkkPerKwh));

        var sut = CreateSut();
        var result = await sut.ResolveAsync(DateTimeOffset.UtcNow);

        Assert.False(result.ElafgiftReducedApplied);
        Assert.Equal(NationwideCharges.NormalElafgiftDkkPerKwh, result.ElafgiftDkkPerKwh);
    }
}
