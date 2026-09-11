using Microsoft.JSInterop;
using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>
/// IndexedDB-backed <see cref="ISpotPriceRepository"/> for the public WASM build — see
/// wwwroot/indexedDbInterop.js. Dates/times are normalized to UTC ISO-8601 strings before being
/// handed to JS (same reason as <c>SpotPriceRepository</c>'s SQL version: composite keys are plain
/// strings, so their sort order has to be correct by string comparison alone).
/// </summary>
public sealed class IndexedDbSpotPriceRepository(IJSRuntime js) : ISpotPriceRepository
{
    public async Task UpsertManyAsync(IEnumerable<SpotPriceRecord> prices, CancellationToken ct = default)
    {
        var rows = prices.Select(ToRow).ToArray();
        // Cast to object: passing a bare array as the sole params arg makes the compiler use it AS
        // the args array itself (array covariance), spreading one JS argument per row instead of
        // passing the whole array as a single JS argument — the cast forces the intended wrapping.
        await js.InvokeVoidAsync("wattsUpDb.spotPrices.upsertMany", ct, (object)rows);
    }

    public async Task<SpotPriceRecord?> GetCurrentAsync(string priceArea, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var row = await js.InvokeAsync<Row?>("wattsUpDb.spotPrices.getCurrent", ct, priceArea, asOfUtc.ToUniversalTime().ToString("O"));
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<SpotPriceRecord>> GetRangeAsync(
        string priceArea, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var rows = await js.InvokeAsync<Row[]>(
            "wattsUpDb.spotPrices.getRange", ct, priceArea, fromUtc.ToUniversalTime().ToString("O"), toUtc.ToUniversalTime().ToString("O"));
        return rows.Select(ToRecord).ToList();
    }

    private static Row ToRow(SpotPriceRecord p) => new(
        p.PriceArea, p.TimeUtc.ToUniversalTime().ToString("O"), p.TimeDk.ToUniversalTime().ToString("O"), (double)p.PriceDkkPerKwh);

    private static SpotPriceRecord ToRecord(Row r) =>
        new(r.PriceArea, DateTimeOffset.Parse(r.TimeUtc), DateTimeOffset.Parse(r.TimeDk), (decimal)r.PriceDkkPerKwh);

    private sealed record Row(string PriceArea, string TimeUtc, string TimeDk, double PriceDkkPerKwh);
}
