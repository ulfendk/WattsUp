using Dapper;

namespace WattsUp.Data.Repositories;

public sealed record ConsumptionDevice(
    string EntityId, string? FriendlyName, string? UnitOfMeasure, string? DeviceClass, bool IsSelected);

public interface IConsumptionDeviceRepository
{
    /// <summary>Upserts the latest list of candidate HA entities seen from the Home Assistant API.
    /// Never touches <see cref="ConsumptionDevice.IsSelected"/> — selection is changed only via
    /// <see cref="SetSelectedAsync"/>.</summary>
    Task UpsertManyAsync(IEnumerable<ConsumptionDevice> devices, CancellationToken ct = default);

    Task<IReadOnlyList<ConsumptionDevice>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ConsumptionDevice>> GetSelectedAsync(CancellationToken ct = default);

    /// <summary>Replaces the full set of selected entity IDs (multi-select, unlike metering points).</summary>
    Task SetSelectedAsync(IEnumerable<string> entityIds, CancellationToken ct = default);
}

public sealed class ConsumptionDeviceRepository(ISqliteConnectionFactory connectionFactory) : IConsumptionDeviceRepository
{
    public async Task UpsertManyAsync(IEnumerable<ConsumptionDevice> devices, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO consumption_devices (entity_id, friendly_name, unit_of_measure, device_class, is_selected)
            VALUES (@EntityId, @FriendlyName, @UnitOfMeasure, @DeviceClass, 0)
            ON CONFLICT (entity_id) DO UPDATE SET
                friendly_name = excluded.friendly_name,
                unit_of_measure = excluded.unit_of_measure,
                device_class = excluded.device_class;
            """,
            devices,
            transaction);

        transaction.Commit();
    }

    public async Task<IReadOnlyList<ConsumptionDevice>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<Row>("SELECT * FROM consumption_devices;");
        return rows.Select(ToDevice).ToList();
    }

    public async Task<IReadOnlyList<ConsumptionDevice>> GetSelectedAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<Row>("SELECT * FROM consumption_devices WHERE is_selected = 1;");
        return rows.Select(ToDevice).ToList();
    }

    public async Task SetSelectedAsync(IEnumerable<string> entityIds, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("UPDATE consumption_devices SET is_selected = 0;", transaction: transaction);
        await connection.ExecuteAsync(
            "UPDATE consumption_devices SET is_selected = 1 WHERE entity_id = @entityId;",
            entityIds.Select(entityId => new { entityId }),
            transaction);
        transaction.Commit();
    }

    private static ConsumptionDevice ToDevice(Row r) =>
        new(r.entity_id, r.friendly_name, r.unit_of_measure, r.device_class, r.is_selected != 0);

    private sealed record Row(
        string entity_id, string? friendly_name, string? unit_of_measure, string? device_class, long is_selected);
}
