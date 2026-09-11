using WattsUp.Services.Settings;

namespace WattsUp.Data.Repositories;

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
