using System.Text.Json;
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
        var row = await connection.QuerySingleAsync<Row>(
            "SELECT * FROM app_settings WHERE id = 1;");

        return new AppSettings
        {
            PriceAreas = JsonSerializer.Deserialize<List<string>>(row.price_areas_json) ?? ["DK1"],
            GridCompanyGln = row.grid_company_gln,
            GridCompanyName = row.grid_company_name,
            ElectricHeatingRegistered = row.electric_heating_registered != 0,
            VatEnabled = row.vat_enabled != 0,
            SupplierMarkupOrePerKwh = (decimal)row.supplier_markup_ore_per_kwh,
            SupplierSubscriptionFeeDkkPerMonth = (decimal)row.supplier_subscription_fee_dkk_month,
            ReducedElafgiftRateDkkPerKwh = (decimal)row.reduced_elafgift_rate_dkk_per_kwh,
            SelectedMeteringPointGsrn = row.selected_metering_point_gsrn,
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE app_settings SET
                price_areas_json = @PriceAreasJson,
                grid_company_gln = @GridCompanyGln,
                grid_company_name = @GridCompanyName,
                electric_heating_registered = @ElectricHeatingRegistered,
                vat_enabled = @VatEnabled,
                supplier_markup_ore_per_kwh = @SupplierMarkupOrePerKwh,
                supplier_subscription_fee_dkk_month = @SupplierSubscriptionFeeDkkPerMonth,
                reduced_elafgift_rate_dkk_per_kwh = @ReducedElafgiftRateDkkPerKwh,
                selected_metering_point_gsrn = @SelectedMeteringPointGsrn
            WHERE id = 1;
            """,
            new
            {
                PriceAreasJson = JsonSerializer.Serialize(settings.PriceAreas),
                settings.GridCompanyGln,
                settings.GridCompanyName,
                ElectricHeatingRegistered = settings.ElectricHeatingRegistered ? 1 : 0,
                VatEnabled = settings.VatEnabled ? 1 : 0,
                settings.SupplierMarkupOrePerKwh,
                settings.SupplierSubscriptionFeeDkkPerMonth,
                settings.ReducedElafgiftRateDkkPerKwh,
                settings.SelectedMeteringPointGsrn,
            });
    }

    // Matches app_settings column names for Dapper's default mapping.
    private sealed record Row(
        long id,
        string price_areas_json,
        string? grid_company_gln,
        string? grid_company_name,
        long electric_heating_registered,
        long vat_enabled,
        double supplier_markup_ore_per_kwh,
        double supplier_subscription_fee_dkk_month,
        double reduced_elafgift_rate_dkk_per_kwh,
        string? selected_metering_point_gsrn);
}
