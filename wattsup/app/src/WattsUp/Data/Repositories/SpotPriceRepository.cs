using Dapper;

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

public sealed class SpotPriceRepository(ISqliteConnectionFactory connectionFactory) : ISpotPriceRepository
{
    public async Task UpsertManyAsync(IEnumerable<SpotPriceRecord> prices, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO spot_prices (price_area, time_utc, time_dk, price_dkk_per_kwh)
            VALUES (@PriceArea, @TimeUtc, @TimeDk, @PriceDkkPerKwh)
            ON CONFLICT (price_area, time_utc) DO UPDATE SET
                time_dk = excluded.time_dk,
                price_dkk_per_kwh = excluded.price_dkk_per_kwh;
            """,
            prices.Select(p => new
            {
                p.PriceArea,
                TimeUtc = p.TimeUtc.ToString("O"),
                TimeDk = p.TimeDk.ToString("O"),
                p.PriceDkkPerKwh,
            }),
            transaction);

        transaction.Commit();
    }

    public async Task<SpotPriceRecord?> GetCurrentAsync(string priceArea, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QueryFirstOrDefaultAsync<Row>(
            """
            SELECT * FROM spot_prices
            WHERE price_area = @priceArea AND time_utc <= @asOf
            ORDER BY time_utc DESC
            LIMIT 1;
            """,
            new { priceArea, asOf = asOfUtc.ToString("O") });

        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<SpotPriceRecord>> GetRangeAsync(
        string priceArea, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<Row>(
            """
            SELECT * FROM spot_prices
            WHERE price_area = @priceArea AND time_utc >= @from AND time_utc < @to
            ORDER BY time_utc ASC;
            """,
            new { priceArea, from = fromUtc.ToString("O"), to = toUtc.ToString("O") });

        return rows.Select(ToRecord).ToList();
    }

    private static SpotPriceRecord ToRecord(Row row) => new(
        row.price_area,
        DateTimeOffset.Parse(row.time_utc),
        DateTimeOffset.Parse(row.time_dk),
        (decimal)row.price_dkk_per_kwh);

    private sealed record Row(string price_area, string time_utc, string time_dk, double price_dkk_per_kwh);
}
