using Dapper;

namespace WattsUp.Data.Repositories;

public sealed record ElafgiftDailyAllowance(string Gsrn, DateOnly Date, decimal KwhAllowance, string Source);

public interface IElafgiftAllowanceRepository
{
    Task UpsertManyAsync(IEnumerable<ElafgiftDailyAllowance> allowances, CancellationToken ct = default);

    /// <summary>The recorded allowance for one day, or null if that day hasn't settled/been computed yet.</summary>
    Task<ElafgiftDailyAllowance?> GetAsync(string gsrn, DateOnly date, CancellationToken ct = default);
}

public sealed class ElafgiftAllowanceRepository(ISqliteConnectionFactory connectionFactory) : IElafgiftAllowanceRepository
{
    public async Task UpsertManyAsync(IEnumerable<ElafgiftDailyAllowance> allowances, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO elafgift_daily_allowance (gsrn, date, kwh_allowance, source)
            VALUES (@Gsrn, @Date, @KwhAllowance, @Source)
            ON CONFLICT (gsrn, date) DO UPDATE SET
                kwh_allowance = excluded.kwh_allowance,
                source = excluded.source;
            """,
            allowances.Select(a => new { a.Gsrn, Date = a.Date.ToString("yyyy-MM-dd"), a.KwhAllowance, a.Source }),
            transaction);

        transaction.Commit();
    }

    public async Task<ElafgiftDailyAllowance?> GetAsync(string gsrn, DateOnly date, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<Row?>(
            "SELECT * FROM elafgift_daily_allowance WHERE gsrn = @gsrn AND date = @date;",
            new { gsrn, date = date.ToString("yyyy-MM-dd") });

        return row is null
            ? null
            : new ElafgiftDailyAllowance(row.gsrn, DateOnly.Parse(row.date), (decimal)row.kwh_allowance, row.source);
    }

    private sealed record Row(string gsrn, string date, double kwh_allowance, string source);
}
