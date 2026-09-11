using WattsUp.Services.Mqtt;

namespace WattsUp.Wasm.Services;

/// <summary>No broker to resolve on the public WASM build — Diagnostics.razor hides the MQTT row
/// entirely for a browser-hosted RuntimeInfo, so this null result is never actually displayed.</summary>
public sealed class NullMqttBrokerResolver : IMqttBrokerResolver
{
    public Task<ResolvedMqttBroker?> ResolveAsync(CancellationToken ct = default) => Task.FromResult<ResolvedMqttBroker?>(null);
}
