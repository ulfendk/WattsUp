namespace WattsUp.Services.Eloverblik;

public sealed record EloverblikMeteringPoint(string Gsrn, string? TypeOfMp, string? Address);

public interface IEloverblikClient
{
    /// <summary>True when an Eloverblik refresh token has been configured via add-on options.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<EloverblikMeteringPoint>> GetMeteringPointsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<(DateOnly Date, decimal Kwh)>> GetDailyConsumptionAsync(
        string gsrn, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}
