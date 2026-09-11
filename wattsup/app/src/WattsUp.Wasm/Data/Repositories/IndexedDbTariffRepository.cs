using System.Text.Json;
using Microsoft.JSInterop;
using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>IndexedDB-backed <see cref="ITariffRepository"/> — see wwwroot/indexedDbInterop.js.</summary>
public sealed class IndexedDbTariffRepository(IJSRuntime js) : ITariffRepository
{
    public async Task UpsertManyAsync(IEnumerable<TariffLineItem> items, CancellationToken ct = default)
    {
        var rows = items.Select(ToRow).ToArray();
        await js.InvokeVoidAsync("wattsUpDb.tariffs.upsertMany", ct, (object)rows);
    }

    public async Task<IReadOnlyList<TariffLineItem>> GetPerKwhRowsAsync(string glnNumber, DateOnly asOfDate, CancellationToken ct = default)
    {
        var all = await GetAllRowsAsync(glnNumber, asOfDate, ct);
        return all.Where(i => i.ChargeClassification == ChargeClassification.PerKwh).ToList();
    }

    public async Task<IReadOnlyList<TariffLineItem>> GetAllRowsAsync(string glnNumber, DateOnly asOfDate, CancellationToken ct = default)
    {
        var rows = await js.InvokeAsync<Row[]>("wattsUpDb.tariffs.getByGln", ct, glnNumber);
        return rows.Select(ToDomain).Where(i => i.CoversDate(asOfDate)).ToList();
    }

    public async Task<TariffLineItem?> GetByChargeTypeCodeAsync(
        string glnNumber, string chargeTypeCode, DateOnly asOfDate, CancellationToken ct = default)
    {
        var all = await GetAllRowsAsync(glnNumber, asOfDate, ct);
        return all
            .Where(i => i.ChargeTypeCode == chargeTypeCode)
            .OrderByDescending(i => i.ValidFrom)
            .FirstOrDefault();
    }

    private static Row ToRow(TariffLineItem item) => new(
        item.GlnNumber, item.ChargeTypeCode, item.ChargeOwner, item.Note, item.Description,
        item.ValidFrom.ToString("yyyy-MM-dd"), item.ValidTo?.ToString("yyyy-MM-dd"), item.VatClass,
        item.ResolutionDuration, JsonSerializer.Serialize(item.Prices),
        item.ChargeClassification.ToString().ToLowerInvariant() switch { "perkwh" => "per_kwh", var other => other },
        item.TransparentInvoicing, item.TaxIndicator, item.FetchedAt.ToUniversalTime().ToString("O"));

    private static TariffLineItem ToDomain(Row row) => new()
    {
        GlnNumber = row.GlnNumber,
        ChargeTypeCode = row.ChargeTypeCode,
        ChargeOwner = row.ChargeOwner,
        Note = row.Note,
        Description = row.Description,
        ValidFrom = DateOnly.Parse(row.ValidFrom),
        ValidTo = row.ValidTo is null ? null : DateOnly.Parse(row.ValidTo),
        VatClass = row.VatClass,
        ResolutionDuration = row.ResolutionDuration,
        Prices = JsonSerializer.Deserialize<List<decimal>>(row.PricesJson) ?? [],
        ChargeClassification = row.ChargeClassification switch
        {
            "per_kwh" => ChargeClassification.PerKwh,
            "subscription" => ChargeClassification.Subscription,
            _ => ChargeClassification.Unknown,
        },
        TransparentInvoicing = row.TransparentInvoicing,
        TaxIndicator = row.TaxIndicator,
        FetchedAt = DateTimeOffset.Parse(row.FetchedAt),
    };

    private sealed record Row(
        string GlnNumber, string ChargeTypeCode, string ChargeOwner, string? Note, string? Description,
        string ValidFrom, string? ValidTo, string? VatClass, string ResolutionDuration, string PricesJson,
        string ChargeClassification, bool TransparentInvoicing, bool TaxIndicator, string FetchedAt);
}
