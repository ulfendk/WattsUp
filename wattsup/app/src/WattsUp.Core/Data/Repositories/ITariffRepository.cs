namespace WattsUp.Data.Repositories;

public enum ChargeClassification
{
    Unknown,
    PerKwh,
    Subscription,
}

public sealed record TariffLineItem
{
    public required string GlnNumber { get; init; }
    public required string ChargeTypeCode { get; init; }
    public required string ChargeOwner { get; init; }
    public string? Note { get; init; }
    public string? Description { get; init; }
    public required DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public string? VatClass { get; init; }

    /// <summary>"PT1H" (24 hourly values in <see cref="Prices"/>) or "P1D" (a single flat value).</summary>
    public required string ResolutionDuration { get; init; }

    public required IReadOnlyList<decimal> Prices { get; init; }
    public ChargeClassification ChargeClassification { get; init; } = ChargeClassification.Unknown;
    public bool TransparentInvoicing { get; init; }
    public bool TaxIndicator { get; init; }
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>Rate for the given hour-of-day (0-23), or the flat daily rate for P1D rows.</summary>
    public decimal RateForHour(int hourOfDay) =>
        ResolutionDuration == "PT1H" ? Prices[hourOfDay] : Prices[0];

    public bool CoversDate(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);
}

public interface ITariffRepository
{
    Task UpsertManyAsync(IEnumerable<TariffLineItem> items, CancellationToken ct = default);

    /// <summary>All per-kWh grid-tariff rows for a GLN covering the given date (usually one, occasionally more).</summary>
    Task<IReadOnlyList<TariffLineItem>> GetPerKwhRowsAsync(string glnNumber, DateOnly asOfDate, CancellationToken ct = default);

    /// <summary>All rows for a GLN (any classification) covering the given date — used by Diagnostics/Settings.</summary>
    Task<IReadOnlyList<TariffLineItem>> GetAllRowsAsync(string glnNumber, DateOnly asOfDate, CancellationToken ct = default);

    Task<TariffLineItem?> GetByChargeTypeCodeAsync(
        string glnNumber, string chargeTypeCode, DateOnly asOfDate, CancellationToken ct = default);
}
