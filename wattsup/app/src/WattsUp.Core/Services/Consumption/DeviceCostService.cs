using WattsUp.Data.Repositories;
using WattsUp.Services.Pricing;

namespace WattsUp.Services.Consumption;

public sealed record DeviceHourlyCost(string EntityId, DateTimeOffset HourUtc, decimal Kwh, decimal CostDkk);

/// <summary>
/// Turns a device's recorded hourly kWh (from <see cref="IDeviceHourlyConsumptionRepository"/>) into
/// DKK cost using the same <see cref="IPriceCalculationService"/> the Dashboard and MQTT publisher
/// use — backlog item 4: "calculate costs on an hourly basis and in real time for the current hour."
/// </summary>
public interface IDeviceCostService
{
    Task<IReadOnlyList<DeviceHourlyCost>> GetHourlyCostsAsync(
        string entityId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Cost for the current (possibly still in-progress) hour, using whatever kWh has been
    /// recorded for it so far.</summary>
    Task<DeviceHourlyCost?> GetCurrentHourCostAsync(string entityId, CancellationToken ct = default);
}

public sealed class DeviceCostService(
    IDeviceHourlyConsumptionRepository consumptionRepository,
    IPriceCalculationService priceCalculationService,
    ISettingsRepository settingsRepository)
    : IDeviceCostService
{
    public async Task<IReadOnlyList<DeviceHourlyCost>> GetHourlyCostsAsync(
        string entityId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var settings = await settingsRepository.GetAsync(ct);
        var readings = await consumptionRepository.GetRangeAsync(entityId, fromUtc, toUtc, ct);

        var costs = new List<DeviceHourlyCost>(readings.Count);
        foreach (var reading in readings)
        {
            costs.Add(await ToCostAsync(settings.PriceArea, reading, ct));
        }
        return costs;
    }

    public async Task<DeviceHourlyCost?> GetCurrentHourCostAsync(string entityId, CancellationToken ct = default)
    {
        var latest = await consumptionRepository.GetLatestAsync(entityId, ct);
        if (latest is null || latest.HourUtc < CurrentHourStart())
        {
            return null;
        }

        var settings = await settingsRepository.GetAsync(ct);
        return await ToCostAsync(settings.PriceArea, latest, ct);
    }

    private async Task<DeviceHourlyCost> ToCostAsync(string priceArea, DeviceHourlyConsumption reading, CancellationToken ct)
    {
        var breakdown = await priceCalculationService.CalculateAsync(priceArea, reading.HourUtc, ct);
        return new DeviceHourlyCost(reading.EntityId, reading.HourUtc, reading.Kwh, reading.Kwh * breakdown.TotalDkkPerKwh);
    }

    private static DateTimeOffset CurrentHourStart()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
    }
}
