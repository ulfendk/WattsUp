using WattsUp.Services.Mqtt;

namespace WattsUp.Wasm.Services;

/// <summary>No MQTT in the public WASM build — nothing to publish to, no broker reachable from a
/// browser tab. Exists only so Settings.razor's existing <c>UnpublishPriceAreaAsync</c> call on a
/// price-area change doesn't need special-casing per host.</summary>
public sealed class NullMqttPublisherService : IMqttPublisherService
{
    public void RequestRepublish()
    {
    }

    public Task UnpublishPriceAreaAsync(string priceArea, CancellationToken ct = default) => Task.CompletedTask;
}
