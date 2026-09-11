namespace WattsUp.Data.Repositories;

public sealed record MeteringPoint(string Gsrn, string? TypeOfMp, string? Address, bool IsSelected);

public interface IMeteringPointRepository
{
    Task UpsertManyAsync(IEnumerable<MeteringPoint> points, CancellationToken ct = default);
    Task<IReadOnlyList<MeteringPoint>> GetAllAsync(CancellationToken ct = default);
    Task SetSelectedAsync(string gsrn, CancellationToken ct = default);
}
