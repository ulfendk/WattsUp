using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WattsUp.Services.Homeassistant;

/// <summary>
/// Talks to Home Assistant's own Core REST API via the Supervisor's <c>/core/*</c> proxy (only
/// reachable from inside an add-on container, and only when <c>homeassistant_api: true</c> is set
/// in config.yaml) — used to list power/energy sensor entities and poll their current state for
/// backlog item 4's consumption-device cost tracking. Deliberately REST, not MQTT: WattsUp is an
/// MQTT *publisher* to HA, not a subscriber to HA's own sensors.
/// </summary>
public sealed class HomeAssistantApiClient(HttpClient httpClient, ILogger<HomeAssistantApiClient> logger)
    : IHomeAssistantApiClient
{
    private static readonly HashSet<string> PowerConsumptionDeviceClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "power", "energy",
    };

    private static readonly HashSet<string> PowerConsumptionUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "W", "kW", "kWh", "Wh",
    };

    private readonly string? _supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_supervisorToken);

    public async Task<IReadOnlyList<HomeAssistantEntity>> GetPowerConsumptionEntitiesAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return [];
        }

        try
        {
            var states = await SendAsync<List<StateDto>>("api/states", ct);
            if (states is null)
            {
                return [];
            }

            return states
                .Where(s => s.EntityId.StartsWith("sensor.", StringComparison.Ordinal))
                .Where(s =>
                    (s.Attributes.DeviceClass is not null && PowerConsumptionDeviceClasses.Contains(s.Attributes.DeviceClass)) ||
                    (s.Attributes.UnitOfMeasurement is not null && PowerConsumptionUnits.Contains(s.Attributes.UnitOfMeasurement)))
                .Select(s => new HomeAssistantEntity(
                    s.EntityId, s.Attributes.FriendlyName, s.Attributes.UnitOfMeasurement, s.Attributes.DeviceClass))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list power/energy entities from Home Assistant");
            return [];
        }
    }

    public async Task<decimal?> GetStateAsync(string entityId, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            var state = await SendAsync<StateDto>($"api/states/{Uri.EscapeDataString(entityId)}", ct);
            return state is not null && decimal.TryParse(state.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read state for {EntityId} from Home Assistant", entityId);
            return null;
        }
    }

    private async Task<T?> SendAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supervisorToken);

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private sealed class StateDto
    {
        [JsonPropertyName("entity_id")] public string EntityId { get; set; } = "";
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("attributes")] public StateAttributesDto Attributes { get; set; } = new();
    }

    private sealed class StateAttributesDto
    {
        [JsonPropertyName("friendly_name")] public string? FriendlyName { get; set; }
        [JsonPropertyName("unit_of_measurement")] public string? UnitOfMeasurement { get; set; }
        [JsonPropertyName("device_class")] public string? DeviceClass { get; set; }
    }
}
