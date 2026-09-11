using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>No HA device consumption in the public WASM build — see
/// <see cref="NullConsumptionDeviceRepository"/>.</summary>
public sealed class NullDeviceHourlyConsumptionRepository : IDeviceHourlyConsumptionRepository
{
    public Task UpsertAsync(DeviceHourlyConsumption reading, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<DeviceHourlyConsumption>> GetRangeAsync(
        string entityId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeviceHourlyConsumption>>([]);

    public Task<DeviceHourlyConsumption?> GetLatestAsync(string entityId, CancellationToken ct = default) =>
        Task.FromResult<DeviceHourlyConsumption?>(null);
}
