namespace WattsUp.Data.Repositories;

public sealed record SpotPriceRecord(
    string PriceArea,
    DateTimeOffset TimeUtc,
    DateTimeOffset TimeDk,
    decimal PriceDkkPerKwh);

public interface ISpotPriceRepository
{
    Task UpsertManyAsync(IEnumerable<SpotPriceRecord> prices, CancellationToken ct = default);

    /// <summary>The most recent period whose start is at or before <paramref name="asOfUtc"/>.</summary>
    Task<SpotPriceRecord?> GetCurrentAsync(string priceArea, DateTimeOffset asOfUtc, CancellationToken ct = default);

    Task<IReadOnlyList<SpotPriceRecord>> GetRangeAsync(
        string priceArea, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
