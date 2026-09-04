using System.Text.Json.Serialization;
using WattsUp.Services.Json;

namespace WattsUp.Services.EnergiDataService.Dto;

/// <summary>Wire shape of one record from the DayAheadPrices dataset.</summary>
public sealed class DayAheadPriceRecord
{
    [JsonPropertyName("TimeUTC")]
    [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
    public DateTimeOffset TimeUtc { get; set; }

    /// <summary>The API's own Danish-local-time rendering of the same instant as
    /// <see cref="TimeUtc"/> — parsed with the same UTC-assuming converter for deterministic,
    /// container-time-zone-independent behaviour, so despite the name this is NOT actually in
    /// Danish local time; nothing in this app currently reads it, only <see cref="TimeUtc"/> is
    /// used, with proper time-zone conversion applied explicitly where local time is needed.</summary>
    [JsonPropertyName("TimeDK")]
    [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
    public DateTimeOffset TimeDk { get; set; }

    [JsonPropertyName("PriceArea")]
    public string PriceArea { get; set; } = "";

    [JsonPropertyName("DayAheadPriceEUR")]
    public decimal? DayAheadPriceEur { get; set; }

    /// <summary>DKK per MWh. Divide by 1000 for DKK/kWh.</summary>
    [JsonPropertyName("DayAheadPriceDKK")]
    public decimal DayAheadPriceDkk { get; set; }
}
