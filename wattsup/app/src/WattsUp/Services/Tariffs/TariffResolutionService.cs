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
    IConsumptionRepository consumptionRepository,
    IElafgiftAllowanceRepository elafgiftAllowanceRepository)
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

        var gsrn = settings.SelectedMeteringPointGsrn;

        // A day that's fully in the past gets one blended rate for its whole 24 hours instead of a
        // hard threshold cutover mid-day (backlog item 13) — Eloverblik only ever publishes a daily
        // total (with at least a day's delay), so placing the crossing point hour-by-hour isn't
        // meaningful even when real allowance data is available. The reduced rate itself is only
        // resolved once we know it's actually needed, to avoid an unnecessary live tariff lookup.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CopenhagenTimeZone).DateTime);
        if (localDate < today)
        {
            var consumptionKwh = await consumptionRepository.GetDailyKwhAsync(gsrn, localDate, ct);
            if (consumptionKwh > 0m)
            {
                var allowanceKwh = await ResolveDailyAllowanceAsync(gsrn, localDate, ct);
                if (consumptionKwh <= allowanceKwh)
                {
                    return (normalRate, false, false);
                }

                var (dayReducedRate, dayFromLiveRow) = await ResolveReducedRateAsync(settings, seeds, localDate, hour, ct);
                var blendedRate = ElafgiftDistributionCalculator.DistributeForDay(allowanceKwh, consumptionKwh, normalRate, dayReducedRate);
                return (blendedRate, true, dayFromLiveRow);
            }
        }

        // Today (still in progress), or a completed day with no recorded consumption yet: fall back
        // to the live year-to-date-vs-4000kWh threshold check.
        var ytdKwh = await consumptionRepository.GetYearToDateKwhAsync(gsrn, localDate, ct);
        if (ytdKwh <= NationwideCharges.ElectricHeatingAnnualThresholdKwh)
        {
            return (normalRate, false, false);
        }

        var (reducedRate, reducedFromLiveRow) = await ResolveReducedRateAsync(settings, seeds, localDate, hour, ct);
        return (reducedRate, true, reducedFromLiveRow);
    }

    /// <summary>Prefers a distinct "reduceret" row from DatahubPricelist if the API has started
    /// publishing one; otherwise falls back to the manually-configured reduced-rate setting.</summary>
    private async Task<(decimal Rate, bool FromLiveRow)> ResolveReducedRateAsync(
        AppSettings settings, IReadOnlyList<NationwideChargeSeed> seeds, DateOnly localDate, int hour, CancellationToken ct)
    {
        var elafgiftSeed = seeds.FirstOrDefault(s => s.ChargeKey == "elafgift");
        var gln = elafgiftSeed?.GlnNumber ?? NationwideCharges.SystemOperatorGln;
        var allElafgiftRows = await tariffRepository.GetAllRowsAsync(gln, localDate, ct);
        var reducedRow = allElafgiftRows.FirstOrDefault(r =>
            r.ChargeTypeCode == (elafgiftSeed?.ChargeTypeCode ?? NationwideCharges.ElafgiftChargeTypeCode) &&
            (r.Note?.Contains("reduceret", StringComparison.OrdinalIgnoreCase) ?? false));

        return reducedRow is not null
            ? (reducedRow.RateForHour(hour), true)
            : (settings.ReducedElafgiftRateDkkPerKwh, false);
    }

    /// <summary>The day's elafgift allowance: a real value from Eloverblik's secondary "elvarme"
    /// metering point when one has been configured and settled for this date (see
    /// <see cref="BackgroundServices.EloverblikConsumptionPollingService"/>), else an approximation
    /// from how much of the household's annual 4000 kWh/year threshold was still unused at the start
    /// of the day.</summary>
    private async Task<decimal> ResolveDailyAllowanceAsync(string gsrn, DateOnly localDate, CancellationToken ct)
    {
        var recorded = await elafgiftAllowanceRepository.GetAsync(gsrn, localDate, ct);
        if (recorded is not null)
        {
            return recorded.KwhAllowance;
        }

        var ytdAtStartOfDay = await consumptionRepository.GetYearToDateKwhAsync(gsrn, localDate.AddDays(-1), ct);
        return Math.Max(0m, NationwideCharges.ElectricHeatingAnnualThresholdKwh - ytdAtStartOfDay);
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
