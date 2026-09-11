using WattsUp.Data.Repositories;
using WattsUp.Services.Diagnostics;
using WattsUp.Services.EnergiDataService;
using WattsUp.Services.Mqtt;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

namespace WattsUp.BackgroundServices;

/// <summary>
/// Fetches DayAheadPrices for the tracked price area and upserts them into <c>spot_prices</c>.
/// Polls hourly, plus a tighter 5-minute sweep between 13:00-14:00 CET, the window in which
/// next-day prices are typically published.
/// </summary>
public sealed class SpotPricePollingService(
    IServiceScopeFactory scopeFactory,
    DiagnosticsStatusService diagnosticsStatus,
    ILogger<SpotPricePollingService> logger)
    : BackgroundService
{
    private static readonly TimeSpan HourlyInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan TightSweepInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);
            await Task.Delay(IsInTightSweepWindow() ? TightSweepInterval : HourlyInterval, stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var client = scope.ServiceProvider.GetRequiredService<IEnergiDataServiceClient>();
        var spotPriceRepository = scope.ServiceProvider.GetRequiredService<ISpotPriceRepository>();
        var mqttPublisher = scope.ServiceProvider.GetService<IMqttPublisherService>();

        try
        {
            var settings = await settingsRepository.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(settings.PriceArea))
            {
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var fromUtc = nowUtc.AddDays(-1);
            var toUtc = nowUtc.AddDays(2);

            var records = await client.GetDayAheadPricesAsync([settings.PriceArea], fromUtc, toUtc, ct);
            var mapped = records.Select(r => new SpotPriceRecord(
                r.PriceArea, r.TimeUtc, r.TimeDk, r.DayAheadPriceDkk / 1000m));

            await spotPriceRepository.UpsertManyAsync(mapped, ct);
            diagnosticsStatus.ReportSpotPriceSuccess();
            mqttPublisher?.RequestRepublish();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Spot price poll failed; keeping last-known-good cached data");
            diagnosticsStatus.ReportSpotPriceFailure(ex.Message);
        }
    }

    private static bool IsInTightSweepWindow()
    {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TariffResolutionService.CopenhagenTimeZone);
        return localNow.Hour == 13;
    }
}
