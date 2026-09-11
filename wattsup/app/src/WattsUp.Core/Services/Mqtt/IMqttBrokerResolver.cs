namespace WattsUp.Services.Mqtt;

public sealed record ResolvedMqttBroker(string Host, int Port, string? Username, string? Password, bool Ssl, string Source);

/// <summary>
/// Broker resolution order: manual HA-option override, if set → Supervisor auto-discovery →
/// disabled (with the caller expected to surface a Diagnostics warning).
/// </summary>
public interface IMqttBrokerResolver
{
    Task<ResolvedMqttBroker?> ResolveAsync(CancellationToken ct = default);
}
