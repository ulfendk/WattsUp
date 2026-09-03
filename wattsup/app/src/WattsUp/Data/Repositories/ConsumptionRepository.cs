using Dapper;

namespace WattsUp.Data.Repositories;

public sealed record ConsumptionReading(string Gsrn, DateOnly Date, decimal Kwh);

public interface IConsumptionRepository
{
    Task UpsertManyAsync(IEnumerable<ConsumptionReading> readings, CancellationToken ct = default);

    /// <summary>Sum of consumption from Jan 1 of <paramref name="asOfDate"/>'s year through that date, inclusive.</summary>
    Task<decimal> GetYearToDateKwhAsync(string gsrn, DateOnly asOfDate, CancellationToken ct = default);
}

public sealed class ConsumptionRepository(ISqliteConnectionFactory connectionFactory) : IConsumptionRepository
{
    public async Task UpsertManyAsync(IEnumerable<ConsumptionReading> readings, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO consumption_readings (gsrn, date, kwh)
            VALUES (@Gsrn, @Date, @Kwh)
            ON CONFLICT (gsrn, date) DO UPDATE SET kwh = excluded.kwh;
            """,
            readings.Select(r => new { r.Gsrn, Date = r.Date.ToString("yyyy-MM-dd"), r.Kwh }),
            transaction);

        transaction.Commit();
    }

    public async Task<decimal> GetYearToDateKwhAsync(string gsrn, DateOnly asOfDate, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var yearStart = new DateOnly(asOfDate.Year, 1, 1).ToString("yyyy-MM-dd");
        var asOf = asOfDate.ToString("yyyy-MM-dd");

        var sum = await connection.ExecuteScalarAsync<double?>(
            """
            SELECT SUM(kwh) FROM consumption_readings
            WHERE gsrn = @gsrn AND date >= @yearStart AND date <= @asOf;
            """,
            new { gsrn, yearStart, asOf });

        return (decimal)(sum ?? 0);
    }
}
