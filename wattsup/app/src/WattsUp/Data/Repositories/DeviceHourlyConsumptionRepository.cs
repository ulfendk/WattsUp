using Dapper;

namespace WattsUp.Data.Repositories;

public sealed class DeviceHourlyConsumptionRepository(ISqliteConnectionFactory connectionFactory)
    : IDeviceHourlyConsumptionRepository
{
    public async Task UpsertAsync(DeviceHourlyConsumption reading, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO device_hourly_consumption (entity_id, hour_utc, kwh)
            VALUES (@EntityId, @HourUtc, @Kwh)
            ON CONFLICT (entity_id, hour_utc) DO UPDATE SET kwh = excluded.kwh;
            """,
            new { reading.EntityId, HourUtc = reading.HourUtc.ToString("O"), reading.Kwh });
    }

    public async Task<IReadOnlyList<DeviceHourlyConsumption>> GetRangeAsync(
        string entityId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<Row>(
            """
            SELECT * FROM device_hourly_consumption
            WHERE entity_id = @entityId AND hour_utc >= @fromUtc AND hour_utc <= @toUtc
            ORDER BY hour_utc;
            """,
            new { entityId, fromUtc = fromUtc.ToString("O"), toUtc = toUtc.ToString("O") });
        return rows.Select(ToReading).ToList();
    }

    public async Task<DeviceHourlyConsumption?> GetLatestAsync(string entityId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<Row?>(
            """
            SELECT * FROM device_hourly_consumption
            WHERE entity_id = @entityId
            ORDER BY hour_utc DESC
            LIMIT 1;
            """,
            new { entityId });
        return row is null ? null : ToReading(row);
    }

    private static DeviceHourlyConsumption ToReading(Row r) =>
        new(r.entity_id, DateTimeOffset.Parse(r.hour_utc), (decimal)r.kwh);

    private sealed record Row(string entity_id, string hour_utc, double kwh);
}
