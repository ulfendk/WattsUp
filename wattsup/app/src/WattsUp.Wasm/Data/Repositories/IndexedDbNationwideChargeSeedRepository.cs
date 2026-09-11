using Microsoft.JSInterop;
using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>IndexedDB-backed <see cref="INationwideChargeSeedRepository"/> — the 3 seed rows are
/// (re-)written by indexedDbInterop.js's <c>wattsUpDb.open()</c> on every startup, mirroring the
/// SQL migration's <c>INSERT OR IGNORE</c> seed.</summary>
public sealed class IndexedDbNationwideChargeSeedRepository(IJSRuntime js) : INationwideChargeSeedRepository
{
    public async Task<IReadOnlyList<NationwideChargeSeed>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await js.InvokeAsync<Row[]>("wattsUpDb.nationwideSeed.getAll", ct);
        return rows.Select(r => new NationwideChargeSeed(
            r.ChargeKey, r.GlnNumber, r.ChargeTypeCode, r.Note, (decimal)r.FallbackRateDkkPerKwh)).ToList();
    }

    private sealed record Row(string ChargeKey, string GlnNumber, string ChargeTypeCode, string Note, double FallbackRateDkkPerKwh);
}
