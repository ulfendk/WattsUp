namespace WattsUp.Data.Repositories;

public sealed record DeviceHourlyConsumption(string EntityId, DateTimeOffset HourUtc, decimal Kwh);

public interface IDeviceHourlyConsumptionRepository
{
    Task UpsertAsync(DeviceHourlyConsumption reading, CancellationToken ct = default);

    /// <summary>Hourly kWh for one device across an inclusive UTC range, ordered by hour.</summary>
    Task<IReadOnlyList<DeviceHourlyConsumption>> GetRangeAsync(
        string entityId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>The most recently recorded hour for a device, or null if none exist yet.</summary>
    Task<DeviceHourlyConsumption?> GetLatestAsync(string entityId, CancellationToken ct = default);
}
