using WattsUp.Data.Repositories;
using WattsUp.Services.Diagnostics;
using WattsUp.Services.EnergiDataService;
using WattsUp.Services.Mqtt;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

namespace WattsUp.BackgroundServices;

/// <summary>
/// Fetches DatahubPricelist rows for the selected grid company GLN plus the nationwide-charge GLN,
/// upserts them, and re-verifies the seeded nationwide charge type codes still resolve to live
/// data. Runs on startup, then daily.
/// </summary>
public sealed class TariffPollingService(
    IServiceScopeFactory scopeFactory,
    DiagnosticsStatusService diagnosticsStatus,
    ILogger<TariffPollingService> logger)
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
        var settingsRepository = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var seedRepository = scope.ServiceProvider.GetRequiredService<INationwideChargeSeedRepository>();
        var client = scope.ServiceProvider.GetRequiredService<IEnergiDataServiceClient>();
        var tariffRepository = scope.ServiceProvider.GetRequiredService<ITariffRepository>();
        var mqttPublisher = scope.ServiceProvider.GetService<IMqttPublisherService>();

        try
        {
            var settings = await settingsRepository.GetAsync(ct);
            var glnNumbers = new HashSet<string> { NationwideCharges.SystemOperatorGln };
            if (!string.IsNullOrWhiteSpace(settings.GridCompanyGln))
            {
                glnNumbers.Add(settings.GridCompanyGln);
            }

            foreach (var gln in glnNumbers)
            {
                var records = await client.GetTariffLineItemsAsync(gln, ct);
                var items = records.Select(r => new TariffLineItem
                {
                    GlnNumber = r.GlnNumber,
                    ChargeTypeCode = r.ChargeTypeCode,
                    ChargeOwner = r.ChargeOwner,
                    Note = r.Note,
                    Description = r.Description,
                    ValidFrom = DateOnly.FromDateTime(r.ValidFrom.UtcDateTime),
                    ValidTo = r.ValidTo is null ? null : DateOnly.FromDateTime(r.ValidTo.Value.UtcDateTime),
                    VatClass = r.VatClass,
                    ResolutionDuration = r.ResolutionDuration,
                    Prices = r.ToPricesArray(),
                    ChargeClassification = TariffClassifier.Classify(r),
                    TransparentInvoicing = r.TransparentInvoicing != 0,
                    TaxIndicator = r.TaxIndicator != 0,
                    FetchedAt = DateTimeOffset.UtcNow,
                });

                await tariffRepository.UpsertManyAsync(items, ct);
            }

            await VerifySeedCodesResolveAsync(seedRepository, tariffRepository, ct);

            diagnosticsStatus.ReportTariffSuccess();
            mqttPublisher?.RequestRepublish();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tariff poll failed; keeping last-known-good cached data");
            diagnosticsStatus.ReportTariffFailure(ex.Message);
        }
    }

    private async Task VerifySeedCodesResolveAsync(
        INationwideChargeSeedRepository seedRepository, ITariffRepository tariffRepository, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var seed in await seedRepository.GetAllAsync(ct))
        {
            var row = await tariffRepository.GetByChargeTypeCodeAsync(seed.GlnNumber, seed.ChargeTypeCode, today, ct);
            if (row is null)
            {
                var warning = $"Nationwide charge '{seed.ChargeKey}' (GLN {seed.GlnNumber}, code {seed.ChargeTypeCode}) " +
                               "no longer resolves in DatahubPricelist — falling back to the seeded rate.";
                logger.LogWarning(warning);
                diagnosticsStatus.AddWarning(warning);
            }
        }
    }
}
