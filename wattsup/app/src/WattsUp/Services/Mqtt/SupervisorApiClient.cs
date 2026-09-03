using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WattsUp.Services.Mqtt;

public sealed record SupervisorMqttService(string Host, int Port, string? Username, string? Password, bool Ssl);

public interface ISupervisorApiClient
{
    /// <summary>True when a SUPERVISOR_TOKEN is present, i.e. we're actually running as an HA add-on.</summary>
    bool IsAvailable { get; }

    Task<SupervisorMqttService?> GetMqttServiceAsync(CancellationToken ct = default);
}

/// <summary>
/// Talks to the Home Assistant Supervisor API (only reachable from inside an add-on container) to
/// auto-discover the MQTT broker via <c>GET http://supervisor/services/mqtt</c>. Requires
/// <c>hassio_api: true</c> and <c>services: [mqtt:want]</c> in config.yaml.
/// </summary>
public sealed class SupervisorApiClient(HttpClient httpClient, ILogger<SupervisorApiClient> logger) : ISupervisorApiClient
{
    private readonly string? _supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_supervisorToken);

    public async Task<SupervisorMqttService?> GetMqttServiceAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "services/mqtt");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supervisorToken);

            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<SupervisorServiceEnvelope>(cancellationToken: ct);

            if (body?.Data is null || string.IsNullOrWhiteSpace(body.Data.Host))
            {
                return null;
            }

            return new SupervisorMqttService(
                body.Data.Host, body.Data.Port, body.Data.Username, body.Data.Password, body.Data.Ssl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query Supervisor for the MQTT service");
            return null;
        }
    }

    private sealed class SupervisorServiceEnvelope
    {
        [JsonPropertyName("data")]
        public SupervisorServiceData? Data { get; set; }
    }

    private sealed class SupervisorServiceData
    {
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; } = 1883;
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("password")] public string? Password { get; set; }
        [JsonPropertyName("ssl")] public bool Ssl { get; set; }
    }
}
