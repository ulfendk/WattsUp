using Microsoft.JSInterop;
using WattsUp.Data.Repositories;
using WattsUp.Services.Settings;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>IndexedDB-backed <see cref="ISettingsRepository"/> — see wwwroot/indexedDbInterop.js.
/// Unlike the SQL version, there's no seeded row on first run, so <see cref="GetAsync"/> falls back
/// to <see cref="AppSettings"/>'s own defaults (matching the SQL migration's defaults) when nothing
/// has been saved yet.</summary>
public sealed class IndexedDbSettingsRepository(IJSRuntime js) : ISettingsRepository
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var row = await js.InvokeAsync<Row?>("wattsUpDb.settings.get", ct);
        if (row is null)
        {
            return new AppSettings();
        }

        return new AppSettings
        {
            PriceArea = row.PriceArea,
            GridCompanyGln = row.GridCompanyGln,
            GridCompanyName = row.GridCompanyName,
            GridCompanySource = row.GridCompanySource,
            // Rows saved before this field existed deserialize it as null.
            GridTariffChargeTypeCode = row.GridTariffChargeTypeCode ?? AppSettings.DefaultGridTariffChargeTypeCode,
            SupplierSource = row.SupplierSource,
            ElectricHeatingRegistered = row.ElectricHeatingRegistered,
            VatEnabled = row.VatEnabled,
            SupplierMarkupOrePerKwh = (decimal)row.SupplierMarkupOrePerKwh,
            SupplierSubscriptionFeeDkkPerMonth = (decimal)row.SupplierSubscriptionFeeDkkPerMonth,
            ReducedElafgiftRateDkkPerKwh = (decimal)row.ReducedElafgiftRateDkkPerKwh,
            SelectedMeteringPointGsrn = row.SelectedMeteringPointGsrn,
            SelectedElafgiftAllowanceMeteringPointGsrn = row.SelectedElafgiftAllowanceGsrn,
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var row = new Row(
            settings.PriceArea, settings.GridCompanyGln, settings.GridCompanyName, settings.GridCompanySource,
            settings.SupplierSource, settings.ElectricHeatingRegistered, settings.VatEnabled,
            (double)settings.SupplierMarkupOrePerKwh, (double)settings.SupplierSubscriptionFeeDkkPerMonth,
            (double)settings.ReducedElafgiftRateDkkPerKwh, settings.SelectedMeteringPointGsrn,
            settings.SelectedElafgiftAllowanceMeteringPointGsrn, settings.GridTariffChargeTypeCode);
        await js.InvokeVoidAsync("wattsUpDb.settings.save", ct, row);
    }

    private sealed record Row(
        string PriceArea, string? GridCompanyGln, string? GridCompanyName, string GridCompanySource,
        string SupplierSource, bool ElectricHeatingRegistered, bool VatEnabled, double SupplierMarkupOrePerKwh,
        double SupplierSubscriptionFeeDkkPerMonth, double ReducedElafgiftRateDkkPerKwh,
        string? SelectedMeteringPointGsrn, string? SelectedElafgiftAllowanceGsrn, string? GridTariffChargeTypeCode);
}
