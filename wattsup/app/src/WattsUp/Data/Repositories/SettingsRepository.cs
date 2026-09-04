using Dapper;
using WattsUp.Services.Settings;

namespace WattsUp.Data.Repositories;

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

public sealed class SettingsRepository(ISqliteConnectionFactory connectionFactory) : ISettingsRepository
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        // Explicit column list, not SELECT * — app_settings still carries the legacy
        // price_areas_json column (kept only so 002's migration can read it once), which Row
        // deliberately doesn't map; Dapper's record/constructor binding requires every selected
        // column to correspond to a constructor parameter, so a stray extra column breaks it.
        var row = await connection.QuerySingleAsync<Row>(
            """
            SELECT id, price_area, grid_company_gln, grid_company_name, grid_company_source, supplier_source,
                   electric_heating_registered, vat_enabled, supplier_markup_ore_per_kwh,
                   supplier_subscription_fee_dkk_month, reduced_elafgift_rate_dkk_per_kwh,
                   selected_metering_point_gsrn, selected_elafgift_allowance_gsrn
            FROM app_settings WHERE id = 1;
            """);

        return new AppSettings
        {
            PriceArea = row.price_area,
            GridCompanyGln = row.grid_company_gln,
            GridCompanyName = row.grid_company_name,
            GridCompanySource = row.grid_company_source,
            SupplierSource = row.supplier_source,
            ElectricHeatingRegistered = row.electric_heating_registered != 0,
            VatEnabled = row.vat_enabled != 0,
            SupplierMarkupOrePerKwh = (decimal)row.supplier_markup_ore_per_kwh,
            SupplierSubscriptionFeeDkkPerMonth = (decimal)row.supplier_subscription_fee_dkk_month,
            ReducedElafgiftRateDkkPerKwh = (decimal)row.reduced_elafgift_rate_dkk_per_kwh,
            SelectedMeteringPointGsrn = row.selected_metering_point_gsrn,
            SelectedElafgiftAllowanceMeteringPointGsrn = row.selected_elafgift_allowance_gsrn,
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE app_settings SET
                price_area = @PriceArea,
                grid_company_gln = @GridCompanyGln,
                grid_company_name = @GridCompanyName,
                grid_company_source = @GridCompanySource,
                supplier_source = @SupplierSource,
                electric_heating_registered = @ElectricHeatingRegistered,
                vat_enabled = @VatEnabled,
                supplier_markup_ore_per_kwh = @SupplierMarkupOrePerKwh,
                supplier_subscription_fee_dkk_month = @SupplierSubscriptionFeeDkkPerMonth,
                reduced_elafgift_rate_dkk_per_kwh = @ReducedElafgiftRateDkkPerKwh,
                selected_metering_point_gsrn = @SelectedMeteringPointGsrn,
                selected_elafgift_allowance_gsrn = @SelectedElafgiftAllowanceMeteringPointGsrn
            WHERE id = 1;
            """,
            new
            {
                settings.PriceArea,
                settings.GridCompanyGln,
                settings.GridCompanyName,
                settings.GridCompanySource,
                settings.SupplierSource,
                ElectricHeatingRegistered = settings.ElectricHeatingRegistered ? 1 : 0,
                VatEnabled = settings.VatEnabled ? 1 : 0,
                settings.SupplierMarkupOrePerKwh,
                settings.SupplierSubscriptionFeeDkkPerMonth,
                settings.ReducedElafgiftRateDkkPerKwh,
                settings.SelectedMeteringPointGsrn,
                settings.SelectedElafgiftAllowanceMeteringPointGsrn,
            });
    }

    // Matches app_settings column names for Dapper's default mapping.
    private sealed record Row(
        long id,
        string price_area,
        string? grid_company_gln,
        string? grid_company_name,
        string grid_company_source,
        string supplier_source,
        long electric_heating_registered,
        long vat_enabled,
        double supplier_markup_ore_per_kwh,
        double supplier_subscription_fee_dkk_month,
        double reduced_elafgift_rate_dkk_per_kwh,
        string? selected_metering_point_gsrn,
        string? selected_elafgift_allowance_gsrn);
}
