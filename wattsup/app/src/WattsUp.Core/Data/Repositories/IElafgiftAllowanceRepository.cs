namespace WattsUp.Data.Repositories;

public sealed record ElafgiftDailyAllowance(string Gsrn, DateOnly Date, decimal KwhAllowance, string Source);

public interface IElafgiftAllowanceRepository
{
    Task UpsertManyAsync(IEnumerable<ElafgiftDailyAllowance> allowances, CancellationToken ct = default);

    /// <summary>The recorded allowance for one day, or null if that day hasn't settled/been computed yet.</summary>
    Task<ElafgiftDailyAllowance?> GetAsync(string gsrn, DateOnly date, CancellationToken ct = default);
}
