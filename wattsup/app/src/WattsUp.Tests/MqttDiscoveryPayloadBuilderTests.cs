using System.Text.Json;
using WattsUp.Services.Mqtt;

namespace WattsUp.Tests;

public class MqttDiscoveryPayloadBuilderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void BuildCheapestPeriodDiscoveryPayload_SetsARealMdiIconAndAttributesTopic(int hours)
    {
        var payload = MqttDiscoveryPayloadBuilder.BuildCheapestPeriodDiscoveryPayload(hours);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // "clock-start-outline" isn't a real MDI icon (only the filled "clock-start" is) and
        // silently renders nothing in Home Assistant instead of erroring — assert the exact,
        // real icon name rather than just "is present".
        Assert.Equal("mdi:clock-start", root.GetProperty("icon").GetString());
        Assert.Equal(
            MqttDiscoveryPayloadBuilder.CheapestPeriodAttributesTopic(hours),
            root.GetProperty("json_attributes_topic").GetString());
        Assert.Equal("timestamp", root.GetProperty("device_class").GetString());
    }

    [Fact]
    public void BuildPriceDiscoveryPayload_SetsARealMdiIcon()
    {
        var payload = MqttDiscoveryPayloadBuilder.BuildPriceDiscoveryPayload("DK1");
        using var doc = JsonDocument.Parse(payload);

        Assert.Equal("mdi:transmission-tower", doc.RootElement.GetProperty("icon").GetString());
    }
}
