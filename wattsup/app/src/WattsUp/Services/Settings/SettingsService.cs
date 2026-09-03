using WattsUp.Data.Repositories;
using WattsUp.Services.Mqtt;

namespace WattsUp.Services.Settings;

public sealed class SettingsService(ISettingsRepository repository, IMqttPublisherService? mqttPublisher) : ISettingsService
{
    public event Action? SettingsChanged;

    public Task<AppSettings> GetAsync(CancellationToken ct = default) => repository.GetAsync(ct);

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await repository.SaveAsync(settings, ct);
        SettingsChanged?.Invoke();
        mqttPublisher?.RequestRepublish();
    }
}
