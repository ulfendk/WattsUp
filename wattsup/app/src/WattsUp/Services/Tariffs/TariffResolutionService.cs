using WattsUp.Data.Repositories;
using WattsUp.Services.Settings;

namespace WattsUp.Services.Tariffs;

public sealed record TariffResolution(
    decimal GridTariffDkkPerKwh,
    bool GridTariffResolved,
    decimal SystemTariffDkkPerKwh,
    bool SystemTariffResolved,
    decimal TransmissionTariffDkkPerKwh,
    bool TransmissionTariffResolved,
    decimal ElafgiftDkkPerKwh,
    bool ElafgiftReducedApplied,
    bool ElafgiftFromLiveReducedRow);

/// <summary>
/// Single source of truth for tariff-row lookups: validity-window selection, hour-column lookup,
/// and the electric-heating elafgift-threshold logic. Both <see cref="Pricing.PriceCalculationService"/>
/// and the MQTT publisher go through this — never duplicate the lookup logic elsewhere.
/// </summary>
public interface ITariffResolutionService
{
    Task<TariffResolution> ResolveAsync(DateTimeOffset atUtc, CancellationToken ct = default);
}

public sealed class TariffResolutionService(
    ITariffRepository tariffRepository,
    INationwideChargeSeedRepository seedRepository,
    ISettingsRepository settingsRepository,
    IConsumptionRepository consumptionRepository)
    : ITariffResolutionService
{
    public static readonly TimeZoneInfo CopenhagenTimeZone = ResolveCopenhagenTimeZone();

    public async Task<TariffResolution> ResolveAsync(DateTimeOffset atUtc, CancellationToken ct = default)
    {
        var localTime = TimeZoneInfo.ConvertTime(atUtc, CopenhagenTimeZone);
        var localDate = DateOnly.FromDateTime(localTime.DateTime);
        var hour = localTime.Hour;

        var settings = await settingsRepository.GetAsync(ct);
        var seeds = await seedRepository.GetAllAsync(ct);

        var (gridTariff, gridResolved) = await ResolveGridTariffAsync(settings, localDate, hour, ct);
        var (systemTariff, systemResolved) = await ResolveNationwideAsync(
            seeds, "system_tariff", NationwideCharges.SystemTariffDkkPerKwh, localDate, hour, ct);
        var (transmissionTariff, transmissionResolved) = await ResolveNationwideAsync(
            seeds, "transmission_tariff", NationwideCharges.TransmissionTariffDkkPerKwh, localDate, hour, ct);

        var elafgift = await ResolveElafgiftAsync(settings, seeds, localDate, hour, ct);

        return new TariffResolution(
            gridTariff, gridResolved,
            systemTariff, systemResolved,
            transmissionTariff, transmissionResolved,
            elafgift.RateDkkPerKwh, elafgift.ReducedApplied, elafgift.FromLiveReducedRow);
    }

    private async Task<(decimal Rate, bool Resolved)> ResolveGridTariffAsync(
        AppSettings settings, DateOnly localDate, int hour, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.GridCompanyGln))
        {
            return (0m, false);
        }

        var rows = await tariffRepository.GetPerKwhRowsAsync(settings.GridCompanyGln, localDate, ct);
        if (rows.Count == 0)
        {
            return (0m, false);
        }

        return (rows.Sum(r => r.RateForHour(hour)), true);
    }

    private async Task<(decimal Rate, bool Resolved)> ResolveNationwideAsync(
        IReadOnlyList<NationwideChargeSeed> seeds, string chargeKey, decimal compileTimeFallback,
        DateOnly localDate, int hour, CancellationToken ct)
    {
        var seed = seeds.FirstOrDefault(s => s.ChargeKey == chargeKey);
        var gln = seed?.GlnNumber ?? NationwideCharges.SystemOperatorGln;
        var code = seed?.ChargeTypeCode ?? chargeKey switch
        {
            "system_tariff" => NationwideCharges.SystemTariffChargeTypeCode,
            "transmission_tariff" => NationwideCharges.TransmissionTariffChargeTypeCode,
            _ => NationwideCharges.ElafgiftChargeTypeCode,
        };

        var row = await tariffRepository.GetByChargeTypeCodeAsync(gln, code, localDate, ct);
        if (row is not null)
        {
            return (row.RateForHour(hour), true);
        }

        // Live lookup failed (e.g. poller hasn't run yet, or the code stopped resolving) — fall back
        // to the seed's cached rate, then to the compile-time confirmed constant as a last resort.
        return (seed?.FallbackRateDkkPerKwh ?? compileTimeFallback, false);
    }

    private async Task<(decimal RateDkkPerKwh, bool ReducedApplied, bool FromLiveReducedRow)> ResolveElafgiftAsync(
        AppSettings settings, IReadOnlyList<NationwideChargeSeed> seeds, DateOnly localDate, int hour, CancellationToken ct)
    {
        var (normalRate, _) = await ResolveNationwideAsync(
            seeds, "elafgift", NationwideCharges.NormalElafgiftDkkPerKwh, localDate, hour, ct);

        if (!settings.ElectricHeatingRegistered || string.IsNullOrWhiteSpace(settings.SelectedMeteringPointGsrn))
        {
            return (normalRate, false, false);
        }

        var ytdKwh = await consumptionRepository.GetYearToDateKwhAsync(settings.SelectedMeteringPointGsrn, localDate, ct);
        if (ytdKwh <= NationwideCharges.ElectricHeatingAnnualThresholdKwh)
        {
            return (normalRate, false, false);
        }

        // Above the threshold: prefer a distinct "reduceret" row from DatahubPricelist if the API has
        // started publishing one; otherwise fall back to the manually-configured reduced-rate setting.
        var elafgiftSeed = seeds.FirstOrDefault(s => s.ChargeKey == "elafgift");
        var gln = elafgiftSeed?.GlnNumber ?? NationwideCharges.SystemOperatorGln;
        var allElafgiftRows = await tariffRepository.GetAllRowsAsync(gln, localDate, ct);
        var reducedRow = allElafgiftRows.FirstOrDefault(r =>
            r.ChargeTypeCode == (elafgiftSeed?.ChargeTypeCode ?? NationwideCharges.ElafgiftChargeTypeCode) &&
            (r.Note?.Contains("reduceret", StringComparison.OrdinalIgnoreCase) ?? false));

        return reducedRow is not null
            ? (reducedRow.RateForHour(hour), true, true)
            : (settings.ReducedElafgiftRateDkkPerKwh, true, false);
    }

    private static TimeZoneInfo ResolveCopenhagenTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }
}
