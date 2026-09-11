using Microsoft.JSInterop;
using WattsUp.Data.Repositories;

namespace WattsUp.Wasm.Data.Repositories;

/// <summary>IndexedDB-backed <see cref="IConsumptionRepository"/> — see wwwroot/indexedDbInterop.js.</summary>
public sealed class IndexedDbConsumptionRepository(IJSRuntime js) : IConsumptionRepository
{
    public async Task UpsertManyAsync(IEnumerable<ConsumptionReading> readings, CancellationToken ct = default)
    {
        var rows = readings.Select(r => new Row(r.Gsrn, r.Date.ToString("yyyy-MM-dd"), (double)r.Kwh)).ToArray();
        await js.InvokeVoidAsync("wattsUpDb.consumption.upsertMany", ct, (object)rows);
    }

    public async Task<decimal> GetYearToDateKwhAsync(string gsrn, DateOnly asOfDate, CancellationToken ct = default)
    {
        var yearStart = new DateOnly(asOfDate.Year, 1, 1).ToString("yyyy-MM-dd");
        var sum = await js.InvokeAsync<double>("wattsUpDb.consumption.sumRange", ct, gsrn, yearStart, asOfDate.ToString("yyyy-MM-dd"));
        return (decimal)sum;
    }

    public async Task<decimal> GetDailyKwhAsync(string gsrn, DateOnly date, CancellationToken ct = default)
    {
        var kwh = await js.InvokeAsync<double>("wattsUpDb.consumption.dailyKwh", ct, gsrn, date.ToString("yyyy-MM-dd"));
        return (decimal)kwh;
    }

    private sealed record Row(string Gsrn, string Date, double Kwh);
}
