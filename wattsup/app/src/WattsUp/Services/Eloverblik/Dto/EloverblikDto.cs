using System.Text.Json.Serialization;

namespace WattsUp.Services.Eloverblik.Dto;

public sealed class TokenResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = "";
}

public sealed class MeteringPointsResponse
{
    [JsonPropertyName("result")]
    public List<MeteringPointDto> Result { get; set; } = [];
}

public sealed class MeteringPointDto
{
    [JsonPropertyName("meteringPointId")]
    public string MeteringPointId { get; set; } = "";

    [JsonPropertyName("typeOfMP")]
    public string? TypeOfMp { get; set; }

    [JsonPropertyName("streetName")]
    public string? StreetName { get; set; }

    [JsonPropertyName("buildingNumber")]
    public string? BuildingNumber { get; set; }

    [JsonPropertyName("postcode")]
    public string? Postcode { get; set; }

    [JsonPropertyName("cityName")]
    public string? CityName { get; set; }

    public string? FormattedAddress =>
        string.IsNullOrWhiteSpace(StreetName) ? null : $"{StreetName} {BuildingNumber}, {Postcode} {CityName}".Trim();
}

// --- Time series (CIM-flavoured) response shape ---

public sealed class TimeSeriesEnvelope
{
    [JsonPropertyName("result")]
    public List<TimeSeriesResultItem> Result { get; set; } = [];
}

public sealed class TimeSeriesResultItem
{
    [JsonPropertyName("MyEnergyData_MarketDocument")]
    public MarketDocument? MarketDocument { get; set; }
}

public sealed class MarketDocument
{
    [JsonPropertyName("TimeSeries")]
    public List<TimeSeries> TimeSeries { get; set; } = [];
}

public sealed class TimeSeries
{
    [JsonPropertyName("Period")]
    public List<Period> Period { get; set; } = [];
}

public sealed class Period
{
    [JsonPropertyName("timeInterval")]
    public TimeInterval? TimeInterval { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("Point")]
    public List<Point> Point { get; set; } = [];
}

public sealed class TimeInterval
{
    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; set; }
}

public sealed class Point
{
    [JsonPropertyName("position")]
    public string Position { get; set; } = "1";

    [JsonPropertyName("out_Quantity.quantity")]
    public string? Quantity { get; set; }
}
