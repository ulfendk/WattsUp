using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WattsUp.Services.Json;

/// <summary>
/// Parses a <see cref="DateTimeOffset"/> from JSON, treating a date-time string with no
/// offset/"Z" suffix as UTC — rather than System.Text.Json's default behaviour of assuming the
/// *process's own local time zone* for such strings. Both EnergiDataService and Eloverblik send
/// "UTC" timestamps without an explicit offset (e.g. "2026-09-04T09:00:00"), and this app runs
/// inside a container whose system time zone is commonly set to match the Home Assistant
/// instance's own zone (e.g. Europe/Copenhagen) — without this converter, every such value was
/// silently mis-tagged with that local zone's current DST offset instead of true UTC, shifting
/// the represented instant by 1-2 hours depending on the time of year. A string that already
/// carries an explicit offset or "Z" is unaffected — this only fills in the missing case.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (string.IsNullOrEmpty(raw))
        {
            throw new JsonException("Expected a non-empty date-time string.");
        }

        return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>The nullable-<see cref="DateTimeOffset"/> counterpart of <see cref="UtcDateTimeOffsetConverter"/>.</summary>
public sealed class NullableUtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    private static readonly UtcDateTimeOffsetConverter Inner = new();

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            Inner.Write(writer, value.Value, options);
        }
    }
}
