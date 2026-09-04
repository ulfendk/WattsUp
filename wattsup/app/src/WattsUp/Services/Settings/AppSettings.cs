namespace WattsUp.Services.Settings;

/// <summary>
/// Non-secret operational settings, editable live from the Settings page and persisted as a
/// singleton row in SQLite. No add-on restart is needed for changes to take effect.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The single tracked price area/region (backlog item 5 — only one region at a time).</summary>
    public string PriceArea { get; init; } = "DK1";

    public string? GridCompanyGln { get; init; }
    public string? GridCompanyName { get; init; }

    /// <summary>Whether <see cref="GridCompanyGln"/>/<see cref="GridCompanyName"/> were auto-filled
    /// from the selected metering point ("metering_point") or entered by hand ("manual").</summary>
    public string GridCompanySource { get; init; } = "manual";

    /// <summary>Whether the supplier fields were auto-filled from the metering point or entered
    /// by hand — see <see cref="GridCompanySource"/>.</summary>
    public string SupplierSource { get; init; } = "manual";

    public bool ElectricHeatingRegistered { get; init; }
    public bool VatEnabled { get; init; } = true;
    public decimal SupplierMarkupOrePerKwh { get; init; }
    public decimal SupplierSubscriptionFeeDkkPerMonth { get; init; }

    /// <summary>
    /// Fallback source for the reduced elafgift rate applied above the 4000 kWh/year electric-heating
    /// threshold, used until/unless DatahubPricelist starts publishing a distinct reduced-rate row.
    /// Defaults to the current normal rate (they are numerically identical under the 2026/2027 relief).
    /// </summary>
    public decimal ReducedElafgiftRateDkkPerKwh { get; init; } = NationwideCharges.NormalElafgiftDkkPerKwh;

    public string? SelectedMeteringPointGsrn { get; init; }

    /// <summary>The (optional) secondary "elvarme" metering point Eloverblik uses to distribute the
    /// 4000 kWh/year elafgift allowance day by day — see <see cref="Tariffs.TariffResolutionService"/>.</summary>
    public string? SelectedElafgiftAllowanceMeteringPointGsrn { get; init; }
}

/// <summary>
/// Confirmed (2026-09-03) nationwide charge figures, all resolved from GLN 5790000432752
/// ("Energinet Systemansvar A/S (SYO)") in DatahubPricelist. Used as seed values and as the
/// compile-time fallback if a live lookup fails entirely.
/// </summary>
public static class NationwideCharges
{
    public const string SystemOperatorGln = "5790000432752";

    public const string SystemTariffChargeTypeCode = "41000";
    public const decimal SystemTariffDkkPerKwh = 0.072m;

    public const string TransmissionTariffChargeTypeCode = "40000";
    public const decimal TransmissionTariffDkkPerKwh = 0.043m;

    public const string ElafgiftChargeTypeCode = "EA-001";
    public const decimal NormalElafgiftDkkPerKwh = 0.008m;

    /// <summary>Electric-heating reduced elafgift applies above this annual consumption threshold.</summary>
    public const decimal ElectricHeatingAnnualThresholdKwh = 4000m;
}
