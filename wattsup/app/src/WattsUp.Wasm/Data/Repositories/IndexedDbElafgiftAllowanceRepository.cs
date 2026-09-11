using Microsoft.JSInterop;
using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>IndexedDB-backed <see cref="IElafgiftAllowanceRepository"/> — see wwwroot/indexedDbInterop.js.</summary>
public sealed class IndexedDbElafgiftAllowanceRepository(IJSRuntime js) : IElafgiftAllowanceRepository
{
    public async Task UpsertManyAsync(IEnumerable<ElafgiftDailyAllowance> allowances, CancellationToken ct = default)
    {
        var rows = allowances.Select(a => new Row(a.Gsrn, a.Date.ToString("yyyy-MM-dd"), (double)a.KwhAllowance, a.Source)).ToArray();
        await js.InvokeVoidAsync("wattsUpDb.elafgift.upsertMany", ct, (object)rows);
    }

    public async Task<ElafgiftDailyAllowance?> GetAsync(string gsrn, DateOnly date, CancellationToken ct = default)
    {
        var row = await js.InvokeAsync<Row?>("wattsUpDb.elafgift.get", ct, gsrn, date.ToString("yyyy-MM-dd"));
        return row is null ? null : new ElafgiftDailyAllowance(row.Gsrn, DateOnly.Parse(row.Date), (decimal)row.KwhAllowance, row.Source);
    }

    private sealed record Row(string Gsrn, string Date, double KwhAllowance, string Source);
}
