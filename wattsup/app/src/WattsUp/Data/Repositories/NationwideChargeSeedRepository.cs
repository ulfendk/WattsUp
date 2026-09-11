using Dapper;

namespace WattsUp.Data.Repositories;

public sealed class NationwideChargeSeedRepository(ISqliteConnectionFactory connectionFactory)
    : INationwideChargeSeedRepository
{
    public async Task<IReadOnlyList<NationwideChargeSeed>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var rows = await connection.QueryAsync<Row>("SELECT * FROM nationwide_charge_seed;");
        return rows.Select(r => new NationwideChargeSeed(
            r.charge_key, r.gln_number, r.charge_type_code, r.note, (decimal)r.fallback_rate_dkk_per_kwh)).ToList();
    }

    private sealed record Row(string charge_key, string gln_number, string charge_type_code, string note, double fallback_rate_dkk_per_kwh);
}
