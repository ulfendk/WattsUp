using WattsUp.Data.Repositories;
using WattsUp.Services.Diagnostics;
using WattsUp.Services.Eloverblik;
using WattsUp.Services.Settings;

namespace WattsUp.BackgroundServices;

/// <summary>
/// Pulls daily consumption for the selected metering point (only if an Eloverblik refresh token is
/// configured), feeding the electric-heating annual-threshold calculation. Runs on startup, then daily.
/// </summary>
public sealed class EloverblikConsumptionPollingService(
    IServiceScopeFactory scopeFactory,
    DiagnosticsStatusService diagnosticsStatus,
    ILogger<EloverblikConsumptionPollingService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var eloverblikClient = scope.ServiceProvider.GetRequiredService<IEloverblikClient>();
        if (!eloverblikClient.IsConfigured)
        {
            return;
        }

        var settingsRepository = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var consumptionRepository = scope.ServiceProvider.GetRequiredService<IConsumptionRepository>();

        try
        {
            var settings = await settingsRepository.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(settings.SelectedMeteringPointGsrn))
            {
                return;
            }

            var toDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var fromDate = new DateOnly(toDate.Year, 1, 1);

            var readings = await eloverblikClient.GetDailyConsumptionAsync(
                settings.SelectedMeteringPointGsrn, fromDate, toDate, ct);

            await consumptionRepository.UpsertManyAsync(
                readings.Select(r => new ConsumptionReading(settings.SelectedMeteringPointGsrn, r.Date, r.Kwh)), ct);

            diagnosticsStatus.ReportConsumptionSuccess();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eloverblik consumption poll failed; keeping last-known-good cached data");
            diagnosticsStatus.ReportConsumptionFailure(ex.Message);
        }
    }
}
