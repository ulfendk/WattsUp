using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using WattsUp.Services.EnergiDataService.Dto;

namespace WattsUp.Services.EnergiDataService;

/// <summary>
/// Client for the public, keyless api.energidataservice.dk REST API. Every dataset endpoint
/// returns the same <c>{ "total": N, "records": [...] }</c> envelope.
/// </summary>
public sealed class EnergiDataServiceClient(HttpClient httpClient, ILogger<EnergiDataServiceClient> logger)
    : IEnergiDataServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DayAheadPriceRecord>> GetDayAheadPricesAsync(
        IReadOnlyCollection<string> priceAreas, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var filter = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["PriceArea"] = [.. priceAreas] });
        var url = "dataset/DayAheadPrices"
            + $"?start={Uri.EscapeDataString(fromUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture))}"
            + $"&end={Uri.EscapeDataString(toUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture))}"
            + $"&filter={Uri.EscapeDataString(filter)}"
            + "&sort=TimeUTC%20ASC"
            + "&limit=20000";

        var envelope = await GetAsync<DayAheadPriceRecord>(url, ct);
        return envelope?.Records ?? [];
    }

    public async Task<IReadOnlyList<DatahubPricelistRecord>> GetTariffLineItemsAsync(string glnNumber, CancellationToken ct = default)
    {
        var filter = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["GLN_Number"] = [glnNumber] });
        var url = "dataset/DatahubPricelist"
            + $"?filter={Uri.EscapeDataString(filter)}"
            + "&sort=ValidFrom%20DESC"
            + "&limit=5000";

        var envelope = await GetAsync<DatahubPricelistRecord>(url, ct);
        return envelope?.Records ?? [];
    }

    // DatahubPricelist carries every historical validity-period row for every charge type for
    // every DSO - hundreds of rows per company. A single page this size is nowhere near enough to
    // reach most companies (confirmed live: the whole dataset is ~440k rows across 69 distinct
    // companies, and a single unsorted 50k-row page only ever surfaced 7 of them). Page through
    // the full dataset instead, sorted for deterministic paging, accumulating distinct pairs.
    private const int GridCompanyPageSize = 50000;
    private const int GridCompanyMaxPages = 20; // ~1M rows of headroom over the ~440k observed live
    private static readonly TimeSpan GridCompanyPageDelay = TimeSpan.FromSeconds(2); // be polite to a free public API

    public async Task<IReadOnlyList<(string GlnNumber, string ChargeOwner)>> GetDistinctGridCompaniesAsync(CancellationToken ct = default)
    {
        // Pull just the two identifying columns to keep each page's payload small. Cached by
        // whoever calls this (Settings page), not re-fetched on every render.
        var distinct = new HashSet<(string GlnNumber, string ChargeOwner)>();

        for (var page = 0; page < GridCompanyMaxPages; page++)
        {
            var offset = page * GridCompanyPageSize;
            var url = "dataset/DatahubPricelist"
                + "?columns=GLN_Number,ChargeOwner"
                + "&sort=GLN_Number"
                + $"&limit={GridCompanyPageSize}&offset={offset}";

            var envelope = await GetAsync<DatahubPricelistRecord>(url, ct);
            if (envelope is null)
            {
                break; // request failed after retries - return whatever we've accumulated so far.
            }

            foreach (var r in envelope.Records)
            {
                if (!string.IsNullOrWhiteSpace(r.GlnNumber) && !string.IsNullOrWhiteSpace(r.ChargeOwner))
                {
                    distinct.Add((r.GlnNumber, r.ChargeOwner));
                }
            }

            if (envelope.Records.Count < GridCompanyPageSize)
            {
                break; // reached the end of the dataset.
            }

            await Task.Delay(GridCompanyPageDelay, ct);
        }

        return distinct.OrderBy(x => x.ChargeOwner, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<ApiResponseEnvelope<T>?> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<ApiResponseEnvelope<T>>(relativeUrl, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EnergiDataService request to {Url} failed", relativeUrl);
            return null;
        }
    }
}
