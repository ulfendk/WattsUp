using WattsUp.Services.Mqtt;

namespace WattsUp.Wasm.Services;

/// <summary>No broker to resolve on the public WASM build — Diagnostics.razor already renders
/// "Not connected" gracefully when this returns null (same as any non-addon run today).</summary>
public sealed class NullMqttBrokerResolver : IMqttBrokerResolver
{
    public Task<ResolvedMqttBroker?> ResolveAsync(CancellationToken ct = default) => Task.FromResult<ResolvedMqttBroker?>(null);
}
