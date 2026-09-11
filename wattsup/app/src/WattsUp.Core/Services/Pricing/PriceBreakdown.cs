namespace WattsUp.Services.Pricing;

/// <summary>The full "actual price" breakdown for one price area at one point in time.</summary>
public sealed record PriceBreakdown
{
    public required string PriceArea { get; init; }

    /// <summary>The instant this breakdown was calculated for — typically "now", not necessarily
    /// aligned to a price period boundary. For the period the resolved spot price actually covers,
    /// see <see cref="PricePeriodStartUtc"/>.</summary>
    public required DateTimeOffset AtUtc { get; init; }

    /// <summary>The start of the spot-price settlement period <see cref="AtUtc"/> falls in (e.g.
    /// 11:15 for an 11:15-11:30 quarter-hour period), or null when no spot price resolved at all.
    /// Denmark's day-ahead market publishes 15-minute periods, so this can differ from
    /// <see cref="AtUtc"/> by up to that period's length — this is "as of" the current price.</summary>
    public DateTimeOffset? PricePeriodStartUtc { get; init; }

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

    /// <summary>The VAT amount itself (25% of <see cref="SubtotalDkkPerKwh"/> when <see cref="VatEnabled"/>,
    /// else 0) — surfaced explicitly so the UI can show it instead of just an "included" flag.</summary>
    public required decimal VatAmountDkkPerKwh { get; init; }

    public required decimal TotalDkkPerKwh { get; init; }

    /// <summary>True if every input was resolved from live/cached data rather than a fallback constant.</summary>
    public bool FullyResolved => SpotPriceResolved && GridTariffResolved && NationwideChargesResolved;
}
