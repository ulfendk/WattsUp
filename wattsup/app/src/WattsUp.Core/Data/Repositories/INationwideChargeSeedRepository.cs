namespace WattsUp.Data.Repositories;

public sealed record NationwideChargeSeed(
    string ChargeKey, string GlnNumber, string ChargeTypeCode, string Note, decimal FallbackRateDkkPerKwh);

public interface INationwideChargeSeedRepository
{
    Task<IReadOnlyList<NationwideChargeSeed>> GetAllAsync(CancellationToken ct = default);
}
