using WattsUp.Data.Repositories;
using WattsUp.Services.Tariffs;

namespace WattsUp.Services.Pricing;

/// <summary>
/// The formula consumed by both the Dashboard and the MQTT publisher — the single source of truth
/// for turning spot price + resolved tariffs + supplier markup into the published "actual price".
/// </summary>
public interface IPriceCalculationService
{
    Task<PriceBreakdown> CalculateAsync(string priceArea, DateTimeOffset atUtc, CancellationToken ct = default);
}

public sealed class PriceCalculationService(
    ISpotPriceRepository spotPriceRepository,
    ITariffResolutionService tariffResolutionService,
    ISettingsRepository settingsRepository)
    : IPriceCalculationService
{
    private const decimal VatMultiplier = 1.25m;

    public async Task<PriceBreakdown> CalculateAsync(string priceArea, DateTimeOffset atUtc, CancellationToken ct = default)
    {
        var settings = await settingsRepository.GetAsync(ct);
        var spotRecord = await spotPriceRepository.GetCurrentAsync(priceArea, atUtc, ct);
        var tariffs = await tariffResolutionService.ResolveAsync(atUtc, ct);

        var spotPrice = spotRecord?.PriceDkkPerKwh ?? 0m;
        var markup = settings.SupplierMarkupOrePerKwh / 100m;

        return Calculate(
            priceArea, atUtc,
            spotPrice, spotRecord is not null,
            tariffs,
            markup,
            settings.VatEnabled,
            spotRecord?.TimeUtc);
    }

    /// <summary>Pure formula, split out so the worked example can be verified without any I/O.</summary>
    public static PriceBreakdown Calculate(
        string priceArea, DateTimeOffset atUtc,
        decimal spotPriceDkkPerKwh, bool spotPriceResolved,
        TariffResolution tariffs,
        decimal markupDkkPerKwh,
        bool vatEnabled,
        DateTimeOffset? pricePeriodStartUtc = null)
    {
        var subtotal = spotPriceDkkPerKwh
            + tariffs.GridTariffDkkPerKwh
            + tariffs.SystemTariffDkkPerKwh
            + tariffs.TransmissionTariffDkkPerKwh
            + tariffs.ElafgiftDkkPerKwh
            + markupDkkPerKwh;

        // Audited (backlog item 3): DayAheadPrices spot prices and DatahubPricelist tariff rows are
        // both published ex-VAT by convention, and TariffResolutionService never applies VAT itself —
        // this is the single place VAT is added, so there's no double-counting.
        var vatAmount = vatEnabled ? subtotal * (VatMultiplier - 1m) : 0m;
        var total = subtotal + vatAmount;

        return new PriceBreakdown
        {
            PriceArea = priceArea,
            AtUtc = atUtc,
            PricePeriodStartUtc = pricePeriodStartUtc,
            SpotPriceDkkPerKwh = spotPriceDkkPerKwh,
            SpotPriceResolved = spotPriceResolved,
            GridTariffDkkPerKwh = tariffs.GridTariffDkkPerKwh,
            GridTariffResolved = tariffs.GridTariffResolved,
            SystemTariffDkkPerKwh = tariffs.SystemTariffDkkPerKwh,
            TransmissionTariffDkkPerKwh = tariffs.TransmissionTariffDkkPerKwh,
            NationwideChargesResolved = tariffs.SystemTariffResolved && tariffs.TransmissionTariffResolved,
            ElafgiftDkkPerKwh = tariffs.ElafgiftDkkPerKwh,
            ElafgiftReducedApplied = tariffs.ElafgiftReducedApplied,
            MarkupDkkPerKwh = markupDkkPerKwh,
            SubtotalDkkPerKwh = subtotal,
            VatEnabled = vatEnabled,
            VatAmountDkkPerKwh = vatAmount,
            TotalDkkPerKwh = total,
        };
    }
}
