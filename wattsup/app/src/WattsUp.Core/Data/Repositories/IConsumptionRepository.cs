namespace WattsUp.Data.Repositories;

public sealed record ConsumptionReading(string Gsrn, DateOnly Date, decimal Kwh);

public interface IConsumptionRepository
{
    Task UpsertManyAsync(IEnumerable<ConsumptionReading> readings, CancellationToken ct = default);

    /// <summary>Sum of consumption from Jan 1 of <paramref name="asOfDate"/>'s year through that date, inclusive.</summary>
    Task<decimal> GetYearToDateKwhAsync(string gsrn, DateOnly asOfDate, CancellationToken ct = default);

    /// <summary>Total consumption recorded for one specific day, or 0 if none is recorded yet.</summary>
    Task<decimal> GetDailyKwhAsync(string gsrn, DateOnly date, CancellationToken ct = default);
}
