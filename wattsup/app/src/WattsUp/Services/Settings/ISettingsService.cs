namespace WattsUp.Services.Settings;

/// <summary>
/// Front door for reading/writing <see cref="AppSettings"/>. Wraps the repository with change
/// notification (for the Blazor UI to refresh) and an MQTT republish trigger — saving a setting
/// takes effect immediately, no add-on restart needed.
/// </summary>
public interface ISettingsService
{
    event Action? SettingsChanged;

    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
