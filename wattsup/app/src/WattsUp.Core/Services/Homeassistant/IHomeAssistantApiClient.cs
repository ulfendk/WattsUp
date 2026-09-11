namespace WattsUp.Services.Homeassistant;

public sealed record HomeAssistantEntity(
    string EntityId, string? FriendlyName, string? UnitOfMeasurement, string? DeviceClass);

public interface IHomeAssistantApiClient
{
    /// <summary>True when a SUPERVISOR_TOKEN is present, i.e. we're actually running as an HA add-on.</summary>
    bool IsAvailable { get; }

    /// <summary>Lists <c>sensor.*</c> entities that look like power/energy consumption sensors
    /// (device_class power/energy, or a W/kW/kWh unit) — candidates for backlog item 4's
    /// consumption-device picker.</summary>
    Task<IReadOnlyList<HomeAssistantEntity>> GetPowerConsumptionEntitiesAsync(CancellationToken ct = default);

    /// <summary>The current numeric state of one entity, or null if unavailable/non-numeric.</summary>
    Task<decimal?> GetStateAsync(string entityId, CancellationToken ct = default);
}
