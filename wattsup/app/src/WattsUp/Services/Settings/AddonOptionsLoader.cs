using System.Text.Json;

namespace WattsUp.Services.Settings;

/// <summary>
/// Reads <c>/data/options.json</c> (the Home Assistant Supervisor's add-on options file) directly
/// with <see cref="System.Text.Json"/> at startup. No bashio, no s6 — the container is plain
/// <c>dotnet WattsUp.dll</c> as PID 1.
/// </summary>
public static class AddonOptionsLoader
{
    /// <summary>
    /// Path to options.json. Overridable via the WATTSUP_OPTIONS_PATH environment variable so
    /// local dev / tests can point at a fixture file instead of the real HA path.
    /// </summary>
    public static string ResolvePath()
        => Environment.GetEnvironmentVariable("WATTSUP_OPTIONS_PATH") ?? "/data/options.json";

    public static AddonOptions Load(ILogger logger)
    {
        var path = ResolvePath();

        if (!File.Exists(path))
        {
            logger.LogWarning(
                "No add-on options file found at {Path}; running with empty defaults (expected during local dev)",
                path);
            return new AddonOptions();
        }

        try
        {
            var json = File.ReadAllText(path);
            var options = JsonSerializer.Deserialize<AddonOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return options ?? new AddonOptions();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read/parse add-on options at {Path}; falling back to empty defaults", path);
            return new AddonOptions();
        }
    }
}
