using System.Text.Json.Serialization;

namespace WattsUp.Services.Settings;

/// <summary>
/// Secrets and deployment-time settings bound from the Home Assistant add-on's
/// <c>/data/options.json</c>. Never persisted to SQLite, never editable from the UI.
/// Property names mirror the snake_case keys in <c>config.yaml</c>'s <c>options</c>/<c>schema</c>.
/// </summary>
public sealed class AddonOptions
{
    [JsonPropertyName("eloverblik_refresh_token")]
    public string EloverblikRefreshToken { get; set; } = "";

    [JsonPropertyName("carnot_api_key")]
    public string CarnotApiKey { get; set; } = "";

    [JsonPropertyName("mqtt_host")]
    public string MqttHost { get; set; } = "";

    [JsonPropertyName("mqtt_port")]
    public int MqttPort { get; set; } = 1883;

    [JsonPropertyName("mqtt_username")]
    public string MqttUsername { get; set; } = "";

    [JsonPropertyName("mqtt_password")]
    public string MqttPassword { get; set; } = "";

    [JsonPropertyName("log_level")]
    public string LogLevel { get; set; } = "info";

    public bool HasEloverblikToken => !string.IsNullOrWhiteSpace(EloverblikRefreshToken);
    public bool HasManualMqttOverride => !string.IsNullOrWhiteSpace(MqttHost);
}
