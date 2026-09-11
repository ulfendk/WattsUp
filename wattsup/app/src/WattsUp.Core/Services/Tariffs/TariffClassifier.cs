using WattsUp.Data.Repositories;
using WattsUp.Services.EnergiDataService.Dto;

namespace WattsUp.Services.Tariffs;

/// <summary>
/// Classifies a DatahubPricelist row as a per-kWh charge (feeds the price calculation) or a flat
/// subscription/"abo" line item (informational only, never published to MQTT), via a keyword
/// heuristic over Note/Description. Rows the heuristic can't confidently place are left "unknown"
/// and surfaced for manual review in Settings/Diagnostics rather than silently guessed.
/// </summary>
public static class TariffClassifier
{
    private static readonly string[] SubscriptionKeywords =
        ["abo", "abonnement", "fastbeløb", "fast beløb", "grundbeløb", "grundgebyr"];

    private static readonly string[] PerKwhKeywords =
        ["tarif", "nettarif", "transport", "elafgift", "systemtarif", "transmission"];

    public static ChargeClassification Classify(DatahubPricelistRecord record)
    {
        var text = $"{record.Note} {record.Description}".ToLowerInvariant();

        if (SubscriptionKeywords.Any(text.Contains))
        {
            return ChargeClassification.Subscription;
        }

        if (PerKwhKeywords.Any(text.Contains))
        {
            return ChargeClassification.PerKwh;
        }

        // Hourly-resolution rows are, in practice, always per-kWh transport tariffs — subscription
        // fees are always flat daily/monthly amounts.
        if (record.ResolutionDuration == "PT1H")
        {
            return ChargeClassification.PerKwh;
        }

        return ChargeClassification.Unknown;
    }
}
