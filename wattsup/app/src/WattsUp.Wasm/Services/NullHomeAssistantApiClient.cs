using WattsUp.Services.Homeassistant;

namespace WattsUp.Wasm.Services;

/// <summary>No Home Assistant integration in the public WASM build — see the plan's decision to
/// drop it entirely (no Supervisor, no broker, nothing to poll). <see cref="IsAvailable"/> being
/// false already makes Settings.razor hide the device picker on its own.</summary>
public sealed class NullHomeAssistantApiClient : IHomeAssistantApiClient
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<HomeAssistantEntity>> GetPowerConsumptionEntitiesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HomeAssistantEntity>>([]);

    public Task<decimal?> GetStateAsync(string entityId, CancellationToken ct = default) =>
        Task.FromResult<decimal?>(null);
}
