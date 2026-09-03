using System.Text.Json;
using System.Text.Json.Serialization;

namespace WattsUp.Services.Mqtt;

/// <summary>Builds Home Assistant MQTT Discovery config payloads and topic names for WattsUp entities.</summary>
public static class MqttDiscoveryPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string AvailabilityTopic = "wattsup/status";

    public static string PriceStateTopic(string priceArea) => $"wattsup/price/{priceArea.ToLowerInvariant()}/state";
    public static string PriceAttributesTopic(string priceArea) => $"wattsup/price/{priceArea.ToLowerInvariant()}/attributes";
    public static string PriceDiscoveryTopic(string priceArea) => $"homeassistant/sensor/wattsup_price_{priceArea.ToLowerInvariant()}/config";
    public static string DiagnosticsStateTopic => "wattsup/diagnostics/state";
    public static string DiagnosticsAttributesTopic => "wattsup/diagnostics/attributes";
    public static string DiagnosticsDiscoveryTopic => "homeassistant/sensor/wattsup_diagnostics/config";

    private static Device DeviceInfo => new(["wattsup"], "WattsUp", "WattsUp");

    public static string BuildPriceDiscoveryPayload(string priceArea)
    {
        var config = new SensorDiscoveryConfig
        {
            Name = $"WattsUp Price {priceArea.ToUpperInvariant()}",
            UniqueId = $"wattsup_price_{priceArea.ToLowerInvariant()}",
            StateTopic = PriceStateTopic(priceArea),
            JsonAttributesTopic = PriceAttributesTopic(priceArea),
            AvailabilityTopic = AvailabilityTopic,
            UnitOfMeasurement = "DKK/kWh",
            DeviceClass = "monetary",
            StateClass = "measurement",
            Icon = "mdi:transmission-tower",
            Device = DeviceInfo,
        };
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static string BuildDiagnosticsDiscoveryPayload()
    {
        var config = new SensorDiscoveryConfig
        {
            Name = "WattsUp Diagnostics",
            UniqueId = "wattsup_diagnostics",
            StateTopic = DiagnosticsStateTopic,
            JsonAttributesTopic = DiagnosticsAttributesTopic,
            AvailabilityTopic = AvailabilityTopic,
            Icon = "mdi:information-outline",
            Device = DeviceInfo,
        };
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed record Device(
        [property: JsonPropertyName("identifiers")] string[] Identifiers,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("manufacturer")] string Manufacturer);

    private sealed record SensorDiscoveryConfig
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("unique_id")] public required string UniqueId { get; init; }
        [JsonPropertyName("state_topic")] public required string StateTopic { get; init; }
        [JsonPropertyName("json_attributes_topic")] public string? JsonAttributesTopic { get; init; }
        [JsonPropertyName("availability_topic")] public required string AvailabilityTopic { get; init; }
        [JsonPropertyName("payload_available")] public string PayloadAvailable { get; init; } = "online";
        [JsonPropertyName("payload_not_available")] public string PayloadNotAvailable { get; init; } = "offline";
        [JsonPropertyName("unit_of_measurement")] public string? UnitOfMeasurement { get; init; }
        [JsonPropertyName("device_class")] public string? DeviceClass { get; init; }
        [JsonPropertyName("state_class")] public string? StateClass { get; init; }
        [JsonPropertyName("icon")] public string? Icon { get; init; }
        [JsonPropertyName("device")] public required Device Device { get; init; }
    }
}
