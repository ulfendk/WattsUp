using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WattsUp.Services.Eloverblik.Dto;
using WattsUp.Services.Settings;

namespace WattsUp.Services.Eloverblik;

/// <summary>
/// Client for the Eloverblik CustomerApi (docs.eloverblik.dk). Exchanges the user's long-lived
/// refresh token (an HA add-on secret) for a 24h data-access token, cached in memory only — cheap
/// to re-derive on restart, so no DB table is needed for it.
/// </summary>
public sealed class EloverblikClient : IEloverblikClient
{
    private readonly HttpClient _httpClient;
    private readonly AddonOptions _options;
    private readonly ILogger<EloverblikClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedAccessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public EloverblikClient(HttpClient httpClient, AddonOptions options, ILogger<EloverblikClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured => _options.HasEloverblikToken;

    public async Task<IReadOnlyList<EloverblikMeteringPoint>> GetMeteringPointsAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var token = await GetAccessTokenAsync(ct);
        if (token is null)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/meteringpoints/meteringpoints?includeAll=false");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<MeteringPointsResponse>(cancellationToken: ct);
            return body?.Result.Select(mp => new EloverblikMeteringPoint(mp.MeteringPointId, mp.TypeOfMp, mp.FormattedAddress)).ToList()
                ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Eloverblik metering points");
            return [];
        }
    }

    public async Task<IReadOnlyList<(DateOnly Date, decimal Kwh)>> GetDailyConsumptionAsync(
        string gsrn, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var token = await GetAccessTokenAsync(ct);
        if (token is null)
        {
            return [];
        }

        var url = $"api/meterdata/gettimeseries/{fromDate:yyyy-MM-dd}/{toDate:yyyy-MM-dd}/Day";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                meteringPoints = new { meteringPoint = new[] { gsrn } },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<TimeSeriesEnvelope>(cancellationToken: ct);
            return ParseDailyReadings(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Eloverblik consumption for {Gsrn}", gsrn);
            return [];
        }
    }

    private static List<(DateOnly Date, decimal Kwh)> ParseDailyReadings(TimeSeriesEnvelope? envelope)
    {
        var readings = new List<(DateOnly, decimal)>();
        if (envelope is null)
        {
            return readings;
        }

        foreach (var result in envelope.Result)
        {
            foreach (var series in result.MarketDocument?.TimeSeries ?? [])
            {
                foreach (var period in series.Period)
                {
                    if (period.TimeInterval is null)
                    {
                        continue;
                    }

                    foreach (var point in period.Point)
                    {
                        if (!int.TryParse(point.Position, out var position) ||
                            !decimal.TryParse(point.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
                        {
                            continue;
                        }

                        // Position 1 == the first day in the period (daily resolution assumed, "Day" aggregation requested).
                        var date = DateOnly.FromDateTime(period.TimeInterval.Start.UtcDateTime.AddDays(position - 1));
                        readings.Add((date, quantity));
                    }
                }
            }
        }

        return readings;
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _cachedAccessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _cachedAccessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "api/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.EloverblikRefreshToken);

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(body?.Result))
            {
                return null;
            }

            _cachedAccessToken = body.Result;
            // Tokens are valid 24h; refresh a little early to avoid edge-of-expiry failures.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(23);
            return _cachedAccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to exchange Eloverblik refresh token for an access token");
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
