using System.Text.Json.Serialization;

namespace WattsUp.Services.EnergiDataService.Dto;

/// <summary>Wire shape of one record from the DatahubPricelist dataset.</summary>
public sealed class DatahubPricelistRecord
{
    [JsonPropertyName("ChargeOwner")]
    public string ChargeOwner { get; set; } = "";

    [JsonPropertyName("GLN_Number")]
    public string GlnNumber { get; set; } = "";

    [JsonPropertyName("ChargeType")]
    public string? ChargeType { get; set; }

    [JsonPropertyName("ChargeTypeCode")]
    public string ChargeTypeCode { get; set; } = "";

    [JsonPropertyName("Note")]
    public string? Note { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("ValidFrom")]
    public DateTimeOffset ValidFrom { get; set; }

    [JsonPropertyName("ValidTo")]
    public DateTimeOffset? ValidTo { get; set; }

    [JsonPropertyName("VATClass")]
    public string? VatClass { get; set; }

    [JsonPropertyName("ResolutionDuration")]
    public string ResolutionDuration { get; set; } = "P1D";

    [JsonPropertyName("TransparentInvoicing")]
    public int TransparentInvoicing { get; set; }

    [JsonPropertyName("TaxIndicator")]
    public int TaxIndicator { get; set; }

    // Price1..Price24 — hourly when ResolutionDuration = PT1H, otherwise only Price1 is populated.
    [JsonPropertyName("Price1")] public decimal? Price1 { get; set; }
    [JsonPropertyName("Price2")] public decimal? Price2 { get; set; }
    [JsonPropertyName("Price3")] public decimal? Price3 { get; set; }
    [JsonPropertyName("Price4")] public decimal? Price4 { get; set; }
    [JsonPropertyName("Price5")] public decimal? Price5 { get; set; }
    [JsonPropertyName("Price6")] public decimal? Price6 { get; set; }
    [JsonPropertyName("Price7")] public decimal? Price7 { get; set; }
    [JsonPropertyName("Price8")] public decimal? Price8 { get; set; }
    [JsonPropertyName("Price9")] public decimal? Price9 { get; set; }
    [JsonPropertyName("Price10")] public decimal? Price10 { get; set; }
    [JsonPropertyName("Price11")] public decimal? Price11 { get; set; }
    [JsonPropertyName("Price12")] public decimal? Price12 { get; set; }
    [JsonPropertyName("Price13")] public decimal? Price13 { get; set; }
    [JsonPropertyName("Price14")] public decimal? Price14 { get; set; }
    [JsonPropertyName("Price15")] public decimal? Price15 { get; set; }
    [JsonPropertyName("Price16")] public decimal? Price16 { get; set; }
    [JsonPropertyName("Price17")] public decimal? Price17 { get; set; }
    [JsonPropertyName("Price18")] public decimal? Price18 { get; set; }
    [JsonPropertyName("Price19")] public decimal? Price19 { get; set; }
    [JsonPropertyName("Price20")] public decimal? Price20 { get; set; }
    [JsonPropertyName("Price21")] public decimal? Price21 { get; set; }
    [JsonPropertyName("Price22")] public decimal? Price22 { get; set; }
    [JsonPropertyName("Price23")] public decimal? Price23 { get; set; }
    [JsonPropertyName("Price24")] public decimal? Price24 { get; set; }

    public IReadOnlyList<decimal> ToPricesArray()
    {
        if (ResolutionDuration != "PT1H")
        {
            return [Price1 ?? 0m];
        }

        decimal[] hourly =
        [
            Price1 ?? 0, Price2 ?? 0, Price3 ?? 0, Price4 ?? 0, Price5 ?? 0, Price6 ?? 0,
            Price7 ?? 0, Price8 ?? 0, Price9 ?? 0, Price10 ?? 0, Price11 ?? 0, Price12 ?? 0,
            Price13 ?? 0, Price14 ?? 0, Price15 ?? 0, Price16 ?? 0, Price17 ?? 0, Price18 ?? 0,
            Price19 ?? 0, Price20 ?? 0, Price21 ?? 0, Price22 ?? 0, Price23 ?? 0, Price24 ?? 0,
        ];
        return hourly;
    }
}
