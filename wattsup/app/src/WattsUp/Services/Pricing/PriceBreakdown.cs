namespace WattsUp.Services.Pricing;

/// <summary>The full "actual price" breakdown for one price area at one point in time.</summary>
public sealed record PriceBreakdown
{
    public required string PriceArea { get; init; }
    public required DateTimeOffset AtUtc { get; init; }

    public required decimal SpotPriceDkkPerKwh { get; init; }
    public required bool SpotPriceResolved { get; init; }

    public required decimal GridTariffDkkPerKwh { get; init; }
    public required bool GridTariffResolved { get; init; }

    public required decimal SystemTariffDkkPerKwh { get; init; }
    public required decimal TransmissionTariffDkkPerKwh { get; init; }
    public required bool NationwideChargesResolved { get; init; }

    public required decimal ElafgiftDkkPerKwh { get; init; }
    public required bool ElafgiftReducedApplied { get; init; }

    public required decimal MarkupDkkPerKwh { get; init; }

    public required decimal SubtotalDkkPerKwh { get; init; }
    public required bool VatEnabled { get; init; }
    public required decimal TotalDkkPerKwh { get; init; }

    /// <summary>True if every input was resolved from live/cached data rather than a fallback constant.</summary>
    public bool FullyResolved => SpotPriceResolved && GridTariffResolved && NationwideChargesResolved;
}
