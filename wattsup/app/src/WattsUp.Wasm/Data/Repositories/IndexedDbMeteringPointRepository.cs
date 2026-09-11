using Microsoft.JSInterop;
using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>IndexedDB-backed <see cref="IMeteringPointRepository"/> — see wwwroot/indexedDbInterop.js.</summary>
public sealed class IndexedDbMeteringPointRepository(IJSRuntime js) : IMeteringPointRepository
{
    public async Task UpsertManyAsync(IEnumerable<MeteringPoint> points, CancellationToken ct = default)
    {
        // Mirrors SQL's upsert: never touch IsSelected on upsert, only via SetSelectedAsync — so
        // re-read each point's current selection state before writing, rather than clobbering it.
        var existing = (await GetAllAsync(ct)).ToDictionary(p => p.Gsrn);
        var rows = points
            .Select(p => new Row(p.Gsrn, p.TypeOfMp, p.Address, existing.TryGetValue(p.Gsrn, out var e) && e.IsSelected))
            .ToArray();
        await js.InvokeVoidAsync("wattsUpDb.meteringPoints.upsertMany", ct, (object)rows);
    }

    public async Task<IReadOnlyList<MeteringPoint>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await js.InvokeAsync<Row[]>("wattsUpDb.meteringPoints.getAll", ct);
        return rows.Select(r => new MeteringPoint(r.Gsrn, r.TypeOfMp, r.Address, r.IsSelected)).ToList();
    }

    public async Task SetSelectedAsync(string gsrn, CancellationToken ct = default) =>
        await js.InvokeVoidAsync("wattsUpDb.meteringPoints.setSelected", ct, gsrn);

    private sealed record Row(string Gsrn, string? TypeOfMp, string? Address, bool IsSelected);
}
