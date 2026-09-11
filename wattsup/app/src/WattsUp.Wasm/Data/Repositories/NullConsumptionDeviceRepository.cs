using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>No HA consumption devices in the public WASM build (nothing populates them — see
/// <see cref="Services.NullHomeAssistantApiClient"/>); exists only so Settings.razor's
/// unconditional <c>GetAllAsync()</c> call has somewhere real to read nothing from.</summary>
public sealed class NullConsumptionDeviceRepository : IConsumptionDeviceRepository
{
    public Task UpsertManyAsync(IEnumerable<ConsumptionDevice> devices, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ConsumptionDevice>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConsumptionDevice>>([]);

    public Task<IReadOnlyList<ConsumptionDevice>> GetSelectedAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConsumptionDevice>>([]);

    public Task SetSelectedAsync(IEnumerable<string> entityIds, CancellationToken ct = default) => Task.CompletedTask;
}
