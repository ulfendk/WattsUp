using Dapper;

namespace WattsUp.Data.Repositories;

public sealed record MeteringPoint(string Gsrn, string? TypeOfMp, string? Address, bool IsSelected);

public interface IMeteringPointRepository
{
    Task UpsertManyAsync(IEnumerable<MeteringPoint> points, CancellationToken ct = default);
    Task<IReadOnlyList<MeteringPoint>> GetAllAsync(CancellationToken ct = default);
    Task SetSelectedAsync(string gsrn, CancellationToken ct = default);
}

public sealed class MeteringPointRepository(ISqliteConnectionFactory connectionFactory) : IMeteringPointRepository
{
    public async Task UpsertManyAsync(IEnumerable<MeteringPoint> points, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO metering_points (gsrn, type_of_mp, address, is_selected)
            VALUES (@Gsrn, @TypeOfMp, @Address, 0)
            ON CONFLICT (gsrn) DO UPDATE SET
                type_of_mp = excluded.type_of_mp,
                address = excluded.address;
            """,
            points,
            transaction);

        transaction.Commit();
    }

    public async Task<IReadOnlyList<MeteringPoint>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<Row>("SELECT * FROM metering_points;");
        return rows.Select(r => new MeteringPoint(r.gsrn, r.type_of_mp, r.address, r.is_selected != 0)).ToList();
    }

    public async Task SetSelectedAsync(string gsrn, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("UPDATE metering_points SET is_selected = 0;", transaction: transaction);
        await connection.ExecuteAsync(
            "UPDATE metering_points SET is_selected = 1 WHERE gsrn = @gsrn;", new { gsrn }, transaction);
        transaction.Commit();
    }

    private sealed record Row(string gsrn, string? type_of_mp, string? address, long is_selected);
}
