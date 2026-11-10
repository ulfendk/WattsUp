using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using Moq;
using WattsUp.Data.Repositories;
using WattsUp.Services.Mqtt;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;

namespace WattsUp.Tests;

public class MqttPublisherServiceTests
{
    private readonly Mock<IMqttBrokerResolver> _brokerResolver = new();
    private readonly Mock<IPriceCalculationService> _priceCalculationService = new();
    private readonly Mock<ISettingsRepository> _settingsRepository = new();
    private readonly Mock<ISpotPriceRepository> _spotPriceRepository = new();
    private readonly Mock<IMqttClient> _mqttClient = new();

    private MqttPublisherService CreateSut() => new(
        _brokerResolver.Object,
        _priceCalculationService.Object,
        _settingsRepository.Object,
        _spotPriceRepository.Object,
        NullLogger<MqttPublisherService>.Instance,
        () => _mqttClient.Object);

    [Fact]
    public async Task UnpublishPriceAreaAsync_AlreadyConnected_PublishesEmptyRetainedDiscoveryPayload()
    {
        _mqttClient.SetupGet(c => c.IsConnected).Returns(true);
        _mqttClient
            .Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, null));

        var sut = CreateSut();

        await sut.UnpublishPriceAreaAsync("DK2");

        _mqttClient.Verify(c => c.PublishAsync(
            It.Is<MqttApplicationMessage>(m =>
                m.Topic == MqttDiscoveryPayloadBuilder.PriceDiscoveryTopic("DK2") &&
                m.Retain &&
                m.Payload.IsEmpty),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UnpublishPriceAreaAsync_NotConnectedAndNoBroker_DoesNotThrowOrPublish()
    {
        _mqttClient.SetupGet(c => c.IsConnected).Returns(false);
        _brokerResolver.Setup(r => r.ResolveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ResolvedMqttBroker?)null);

        var sut = CreateSut();

        await sut.UnpublishPriceAreaAsync("DK2");

        _mqttClient.Verify(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishCycle_PublishesUpcomingPricesFromTheCurrentPeriodAndForwardOnlyCheapestWindows()
    {
        var published = new List<MqttApplicationMessage>();
        _mqttClient.SetupGet(c => c.IsConnected).Returns(true);
        _mqttClient
            .Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MqttApplicationMessage, CancellationToken>((m, _) => { lock (published) { published.Add(m); } })
            .ReturnsAsync(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, null, null));
        _settingsRepository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AppSettings { PriceArea = "DK2" });

        var now = DateTimeOffset.UtcNow;
        var periodStart = now.AddTicks(-(now.Ticks % TimeSpan.FromMinutes(15).Ticks));
        _priceCalculationService
            .Setup(s => s.CalculateAsync("DK2", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateTimeOffset at, CancellationToken _) =>
                Breakdown(at, periodStart, at >= periodStart.AddHours(1) && at < periodStart.AddHours(2) ? 1m : 3m));

        _spotPriceRepository
            .Setup(r => r.GetRangeAsync("DK2", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        // Only periods from the current one onwards may be asked for — earlier ones are already over.
        _spotPriceRepository
            .Setup(r => r.GetRangeAsync("DK2", periodStart, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 12)
                .Select(i => new SpotPriceRecord("DK2", periodStart.AddMinutes(15 * i), periodStart.AddMinutes(15 * i), 1m))
                .ToList());

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await sut.StopAsync(CancellationToken.None);

        string Payload(string topic)
        {
            lock (published)
            {
                return Encoding.UTF8.GetString(published.First(m => m.Topic == topic).Payload);
            }
        }

        using var attributes = JsonDocument.Parse(Payload(MqttDiscoveryPayloadBuilder.PriceAttributesTopic("DK2")));
        var upcoming = attributes.RootElement.GetProperty("upcoming_prices");
        Assert.Equal(12, upcoming.GetArrayLength());
        Assert.Equal(periodStart, upcoming[0].GetProperty("start").GetDateTimeOffset());
        Assert.Equal(1m, upcoming[4].GetProperty("price").GetDecimal());
        Assert.Equal(periodStart.AddHours(1),
            DateTimeOffset.Parse(Payload(MqttDiscoveryPayloadBuilder.CheapestPeriodStateTopic(1))));
        lock (published)
        {
            var clock = published.First(m => m.Topic == MqttDiscoveryPayloadBuilder.PriceClockImageTopic);
            Assert.True(clock.Retain);
            Assert.Equal(0x89, clock.Payload.FirstSpan[0]);
        }
    }

    private static PriceBreakdown Breakdown(DateTimeOffset at, DateTimeOffset periodStart, decimal total) => new()
    {
        PriceArea = "DK2",
        AtUtc = at,
        PricePeriodStartUtc = periodStart,
        SpotPriceDkkPerKwh = total,
        SpotPriceResolved = true,
        GridTariffDkkPerKwh = 0m,
        GridTariffResolved = true,
        SystemTariffDkkPerKwh = 0m,
        TransmissionTariffDkkPerKwh = 0m,
        NationwideChargesResolved = true,
        ElafgiftDkkPerKwh = 0m,
        ElafgiftReducedApplied = false,
        MarkupDkkPerKwh = 0m,
        SubtotalDkkPerKwh = total,
        VatEnabled = false,
        VatAmountDkkPerKwh = 0m,
        TotalDkkPerKwh = total,
    };
}
