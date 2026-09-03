using System.Text.Json;
using Dapper;

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

public sealed class TariffRepository(ISqliteConnectionFactory connectionFactory) : ITariffRepository
{
    public async Task UpsertManyAsync(IEnumerable<TariffLineItem> items, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT INTO tariff_line_items
                (gln_number, charge_type_code, valid_from, valid_to, charge_owner, note, description,
                 vat_class, resolution_duration, prices_json, charge_classification,
                 transparent_invoicing, tax_indicator, fetched_at)
            VALUES
                (@GlnNumber, @ChargeTypeCode, @ValidFrom, @ValidTo, @ChargeOwner, @Note, @Description,
                 @VatClass, @ResolutionDuration, @PricesJson, @ChargeClassification,
                 @TransparentInvoicing, @TaxIndicator, @FetchedAt)
            ON CONFLICT (gln_number, charge_type_code, valid_from) DO UPDATE SET
                valid_to = excluded.valid_to,
                charge_owner = excluded.charge_owner,
                note = excluded.note,
                description = excluded.description,
                vat_class = excluded.vat_class,
                resolution_duration = excluded.resolution_duration,
                prices_json = excluded.prices_json,
                charge_classification = excluded.charge_classification,
                transparent_invoicing = excluded.transparent_invoicing,
                tax_indicator = excluded.tax_indicator,
                fetched_at = excluded.fetched_at;
            """,
            items.Select(ToParams),
            transaction);

        transaction.Commit();
    }

    public async Task<IReadOnlyList<TariffLineItem>> GetPerKwhRowsAsync(
        string glnNumber, DateOnly asOfDate, CancellationToken ct = default)
    {
        var all = await GetAllRowsAsync(glnNumber, asOfDate, ct);
        return all.Where(i => i.ChargeClassification == ChargeClassification.PerKwh).ToList();
    }

    public async Task<IReadOnlyList<TariffLineItem>> GetAllRowsAsync(
        string glnNumber, DateOnly asOfDate, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var asOf = asOfDate.ToString("yyyy-MM-dd");
        var rows = await connection.QueryAsync<Row>(
            """
            SELECT * FROM tariff_line_items
            WHERE gln_number = @glnNumber
              AND valid_from <= @asOf
              AND (valid_to IS NULL OR valid_to >= @asOf);
            """,
            new { glnNumber, asOf });

        return rows.Select(ToDomain).ToList();
    }

    public async Task<TariffLineItem?> GetByChargeTypeCodeAsync(
        string glnNumber, string chargeTypeCode, DateOnly asOfDate, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        var asOf = asOfDate.ToString("yyyy-MM-dd");
        var row = await connection.QueryFirstOrDefaultAsync<Row>(
            """
            SELECT * FROM tariff_line_items
            WHERE gln_number = @glnNumber AND charge_type_code = @chargeTypeCode
              AND valid_from <= @asOf
              AND (valid_to IS NULL OR valid_to >= @asOf)
            ORDER BY valid_from DESC
            LIMIT 1;
            """,
            new { glnNumber, chargeTypeCode, asOf });

        return row is null ? null : ToDomain(row);
    }

    private static object ToParams(TariffLineItem item) => new
    {
        item.GlnNumber,
        item.ChargeTypeCode,
        ValidFrom = item.ValidFrom.ToString("yyyy-MM-dd"),
        ValidTo = item.ValidTo?.ToString("yyyy-MM-dd"),
        item.ChargeOwner,
        item.Note,
        item.Description,
        item.VatClass,
        item.ResolutionDuration,
        PricesJson = JsonSerializer.Serialize(item.Prices),
        ChargeClassification = item.ChargeClassification.ToString().ToLowerInvariant() switch
        {
            "perkwh" => "per_kwh",
            var other => other,
        },
        TransparentInvoicing = item.TransparentInvoicing ? 1 : 0,
        TaxIndicator = item.TaxIndicator ? 1 : 0,
        FetchedAt = item.FetchedAt.ToString("O"),
    };

    private static TariffLineItem ToDomain(Row row) => new()
    {
        GlnNumber = row.gln_number,
        ChargeTypeCode = row.charge_type_code,
        ChargeOwner = row.charge_owner,
        Note = row.note,
        Description = row.description,
        ValidFrom = DateOnly.Parse(row.valid_from),
        ValidTo = row.valid_to is null ? null : DateOnly.Parse(row.valid_to),
        VatClass = row.vat_class,
        ResolutionDuration = row.resolution_duration,
        Prices = JsonSerializer.Deserialize<List<decimal>>(row.prices_json) ?? [],
        ChargeClassification = row.charge_classification switch
        {
            "per_kwh" => ChargeClassification.PerKwh,
            "subscription" => ChargeClassification.Subscription,
            _ => ChargeClassification.Unknown,
        },
        TransparentInvoicing = row.transparent_invoicing != 0,
        TaxIndicator = row.tax_indicator != 0,
        FetchedAt = DateTimeOffset.Parse(row.fetched_at),
    };

    private sealed record Row(
        string gln_number, string charge_type_code, string valid_from, string? valid_to,
        string charge_owner, string? note, string? description, string? vat_class,
        string resolution_duration, string prices_json, string charge_classification,
        long transparent_invoicing, long tax_indicator, string fetched_at);
}
