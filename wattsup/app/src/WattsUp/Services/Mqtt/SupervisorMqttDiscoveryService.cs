using WattsUp.Services.Settings;

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

public sealed class SupervisorMqttDiscoveryService(AddonOptions options, ISupervisorApiClient supervisorApiClient) : IMqttBrokerResolver
{
    public async Task<ResolvedMqttBroker?> ResolveAsync(CancellationToken ct = default)
    {
        if (options.HasManualMqttOverride)
        {
            // No mqtt_ssl add-on option exists yet — manual overrides are plain TCP only for now.
            return new ResolvedMqttBroker(
                options.MqttHost, options.MqttPort,
                NullIfEmpty(options.MqttUsername), NullIfEmpty(options.MqttPassword),
                Ssl: false,
                "manual");
        }

        var supervisorMqtt = await supervisorApiClient.GetMqttServiceAsync(ct);
        if (supervisorMqtt is not null)
        {
            return new ResolvedMqttBroker(
                supervisorMqtt.Host, supervisorMqtt.Port, supervisorMqtt.Username, supervisorMqtt.Password,
                supervisorMqtt.Ssl, "supervisor");
        }

        return null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
