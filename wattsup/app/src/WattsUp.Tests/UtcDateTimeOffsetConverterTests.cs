using System.Text.Json;
using System.Text.Json.Serialization;
using WattsUp.Services.Json;

namespace WattsUp.Tests;

public class UtcDateTimeOffsetConverterTests
{
    private sealed class Wrapper
    {
        [JsonConverter(typeof(UtcDateTimeOffsetConverter))]
        public DateTimeOffset Value { get; set; }
    }

    private sealed class NullableWrapper
    {
        [JsonConverter(typeof(NullableUtcDateTimeOffsetConverter))]
        public DateTimeOffset? Value { get; set; }
    }

    // The actual bug this guards against: System.Text.Json's built-in DateTimeOffset converter
    // assumes the PROCESS's local system time zone for an offset-less string (verified against
    // real Energinet API responses, which never include an offset/"Z" suffix even though the
    // field is genuinely UTC) — inside a container whose system time zone is set to e.g.
    // Europe/Copenhagen (common for HA add-ons, matching the HA instance's own configured zone),
    // that silently shifted every parsed timestamp by 1-2 hours depending on DST. This test
    // doesn't (can't reliably) flip the process's own time zone mid-run, but the converter's
    // behavior is independent of it by construction (DateTimeStyles.AssumeUniversal, not ambient
    // TimeZoneInfo.Local) — asserting the exact UTC offset here is what actually matters.
    [Fact]
    public void Read_OffsetLessString_IsAlwaysParsedAsUtc()
    {
        var json = """{"Value":"2026-09-04T09:00:00"}""";

        var result = JsonSerializer.Deserialize<Wrapper>(json)!;

        Assert.Equal(TimeSpan.Zero, result.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), result.Value);
    }

    [Theory]
    [InlineData("2026-09-04T09:00:00Z")]
    [InlineData("2026-09-04T11:00:00+02:00")]
    public void Read_StringWithExplicitOffset_PreservesTheSameInstant(string raw)
    {
        var json = $$"""{"Value":"{{raw}}"}""";

        var result = JsonSerializer.Deserialize<Wrapper>(json)!;

        Assert.Equal(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), result.Value);
    }

    [Fact]
    public void Read_Nullable_NullToken_StaysNull()
    {
        var json = """{"Value":null}""";

        var result = JsonSerializer.Deserialize<NullableWrapper>(json)!;

        Assert.Null(result.Value);
    }

    [Fact]
    public void Read_Nullable_OffsetLessString_IsParsedAsUtc()
    {
        var json = """{"Value":"2026-09-04T09:00:00"}""";

        var result = JsonSerializer.Deserialize<NullableWrapper>(json)!;

        Assert.Equal(new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), result.Value);
    }

    [Fact]
    public void RoundTrip_NormalizesToUtcOffsetAndPreservesTheInstant()
    {
        var original = new Wrapper { Value = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.FromHours(2)) };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<Wrapper>(json)!;

        Assert.Equal(TimeSpan.Zero, roundTripped.Value.Offset);
        Assert.Equal(original.Value, roundTripped.Value); // same absolute instant either way
    }
}
