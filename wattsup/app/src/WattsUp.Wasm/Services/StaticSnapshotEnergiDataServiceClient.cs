using System.Net.Http.Json;
using WattsUp.Services.EnergiDataService;
using WattsUp.Services.EnergiDataService.Dto;

namespace WattsUp.Wasm.Services;

/// <summary>
/// WASM-host <see cref="IEnergiDataServiceClient"/>: api.energidataservice.dk does not send CORS
/// headers (confirmed against the live API, not assumed — a plain browser <c>fetch()</c> to it
/// fails), so a browser tab can never call it directly, unlike everything else this app talks to.
/// Instead, this reads static JSON snapshots of the same public dataset, refreshed periodically by
/// a GitHub Actions job (<c>.github/scripts/fetch_price_snapshot.py</c>) and published alongside
/// the site at <c>data/*.json</c> — a same-origin fetch, so no CORS problem. The snapshot files use
/// the exact same wire shape as the live API, so no bespoke parsing is needed here.
/// </summary>
public sealed class StaticSnapshotEnergiDataServiceClient(HttpClient httpClient, ILogger<StaticSnapshotEnergiDataServiceClient> logger)
    : IEnergiDataServiceClient
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<DayAheadPriceRecord>? _dayAheadPrices;
    private IReadOnlyList<DatahubPricelistRecord>? _tariffLineItems;
    private IReadOnlyList<(string GlnNumber, string ChargeOwner)>? _gridCompanies;

    public async Task<IReadOnlyList<DayAheadPriceRecord>> GetDayAheadPricesAsync(
        IReadOnlyCollection<string> priceAreas, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var all = await LoadDayAheadPricesAsync(ct);
        return all
            .Where(r => priceAreas.Contains(r.PriceArea) && r.TimeUtc >= fromUtc && r.TimeUtc < toUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<DatahubPricelistRecord>> GetTariffLineItemsAsync(string glnNumber, CancellationToken ct = default)
    {
        var all = await LoadTariffLineItemsAsync(ct);
        return all.Where(r => r.GlnNumber == glnNumber).ToList();
    }

    public async Task<IReadOnlyList<(string GlnNumber, string ChargeOwner)>> GetDistinctGridCompaniesAsync(CancellationToken ct = default)
    {
        if (_gridCompanies is not null)
        {
            return _gridCompanies;
        }

        await _lock.WaitAsync(ct);
        try
        {
            _gridCompanies ??= await FetchAsync<List<CompanyDto>>("data/grid-companies.json", ct) switch
            {
                { } rows => rows.Select(r => (r.GlnNumber, r.ChargeOwner)).ToList(),
                null => [],
            };
            return _gridCompanies;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<DayAheadPriceRecord>> LoadDayAheadPricesAsync(CancellationToken ct)
    {
        if (_dayAheadPrices is not null)
        {
            return _dayAheadPrices;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var envelope = await FetchAsync<ApiResponseEnvelope<DayAheadPriceRecord>>("data/day-ahead-prices.json", ct);
            _dayAheadPrices ??= envelope?.Records ?? [];
            return _dayAheadPrices;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<DatahubPricelistRecord>> LoadTariffLineItemsAsync(CancellationToken ct)
    {
        if (_tariffLineItems is not null)
        {
            return _tariffLineItems;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var envelope = await FetchAsync<ApiResponseEnvelope<DatahubPricelistRecord>>("data/tariff-line-items.json", ct);
            _tariffLineItems ??= envelope?.Records ?? [];
            return _tariffLineItems;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<T?> FetchAsync<T>(string relativeUrl, CancellationToken ct)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<T>(relativeUrl, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load {Url} — the scheduled snapshot job may not have run yet", relativeUrl);
            return default;
        }
    }

    private sealed record CompanyDto(string GlnNumber, string ChargeOwner);
}
