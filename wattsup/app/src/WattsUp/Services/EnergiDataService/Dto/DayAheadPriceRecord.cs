using System.Text.Json.Serialization;

namespace WattsUp.Services.EnergiDataService.Dto;

/// <summary>Wire shape of one record from the DayAheadPrices dataset.</summary>
public sealed class DayAheadPriceRecord
{
    [JsonPropertyName("TimeUTC")]
    public DateTimeOffset TimeUtc { get; set; }

    [JsonPropertyName("TimeDK")]
    public DateTimeOffset TimeDk { get; set; }

    [JsonPropertyName("PriceArea")]
    public string PriceArea { get; set; } = "";

    [JsonPropertyName("DayAheadPriceEUR")]
    public decimal? DayAheadPriceEur { get; set; }

    /// <summary>DKK per MWh. Divide by 1000 for DKK/kWh.</summary>
    [JsonPropertyName("DayAheadPriceDKK")]
    public decimal DayAheadPriceDkk { get; set; }
}
