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

    public async Task<IReadOnlyList<(string GlnNumber, string ChargeOwner)>> GetDistinctGridCompaniesAsync(CancellationToken ct = default)
    {
        // Best-effort: pull just the two identifying columns to keep the payload small, then
        // de-duplicate client-side. Cached by whoever calls this (Settings page), not re-fetched
        // on every render.
        var url = "dataset/DatahubPricelist?columns=GLN_Number,ChargeOwner&limit=50000";
        var envelope = await GetAsync<DatahubPricelistRecord>(url, ct);
        if (envelope is null)
        {
            return [];
        }

        return envelope.Records
            .Where(r => !string.IsNullOrWhiteSpace(r.GlnNumber) && !string.IsNullOrWhiteSpace(r.ChargeOwner))
            .Select(r => (r.GlnNumber, r.ChargeOwner))
            .Distinct()
            .OrderBy(x => x.ChargeOwner, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
