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
}
