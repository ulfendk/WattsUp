using System.Text.Json.Serialization;

namespace WattsUp.Services.EnergiDataService.Dto;

/// <summary>The <c>{ "total": N, "records": [...] }</c> envelope every energidataservice.dk dataset endpoint returns.</summary>
public sealed class ApiResponseEnvelope<T>
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("records")]
    public List<T> Records { get; set; } = [];
}
