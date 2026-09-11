namespace WattsUp.Data.Repositories;

public sealed record ConsumptionDevice(
    string EntityId, string? FriendlyName, string? UnitOfMeasure, string? DeviceClass, bool IsSelected);

public interface IConsumptionDeviceRepository
{
    /// <summary>Upserts the latest list of candidate HA entities seen from the Home Assistant API.
    /// Never touches <see cref="ConsumptionDevice.IsSelected"/> — selection is changed only via
    /// <see cref="SetSelectedAsync"/>.</summary>
    Task UpsertManyAsync(IEnumerable<ConsumptionDevice> devices, CancellationToken ct = default);

    Task<IReadOnlyList<ConsumptionDevice>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ConsumptionDevice>> GetSelectedAsync(CancellationToken ct = default);

    /// <summary>Replaces the full set of selected entity IDs (multi-select, unlike metering points).</summary>
    Task SetSelectedAsync(IEnumerable<string> entityIds, CancellationToken ct = default);
}
