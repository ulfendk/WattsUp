using System.Globalization;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using WattsUp.Data.Repositories;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;

namespace WattsUp.Services.Mqtt;

/// <summary>
/// Owns a single plain <see cref="IMqttClient"/> for the app's lifetime (MQTTnet 5.x dropped the
/// ManagedClient/auto-reconnect extension — no version compatible with 5.x exists). Publishes HA
/// MQTT Discovery config once per tracked entity (retained) after each (re)connect, then
/// recomputes and republishes state + attributes on every poll refresh, on each 15-minute
/// settlement boundary, and immediately on any settings change (via <see cref="RequestRepublish"/>)
/// — no add-on restart needed. Unlike a fire-and-forget connect, this checks connectivity on every
/// loop iteration and reconnects if the broker dropped — WattsUp's price data stays actionable
/// continuously, so a silently-dead MQTT connection until the next restart isn't acceptable here.
/// </summary>
public sealed class MqttPublisherService : BackgroundService, IMqttPublisherService
{
    private static readonly TimeSpan BoundaryInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BrokerRetryInterval = TimeSpan.FromMinutes(1);

    private readonly IMqttBrokerResolver _brokerResolver;
    private readonly IPriceCalculationService _priceCalculationService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<MqttPublisherService> _logger;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _republishSignal = new(0, int.MaxValue);
    private readonly HashSet<string> _discoveredPriceAreas = [];
    private bool _diagnosticsDiscoveryPublished;

    public MqttPublisherService(
        IMqttBrokerResolver brokerResolver,
        IPriceCalculationService priceCalculationService,
        ISettingsRepository settingsRepository,
        ILogger<MqttPublisherService> logger)
    {
        _brokerResolver = brokerResolver;
        _priceCalculationService = priceCalculationService;
        _settingsRepository = settingsRepository;
        _logger = logger;

        _client = new MqttClientFactory().CreateMqttClient();
    }

    public void RequestRepublish() => _republishSignal.Release();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                if (!_client.IsConnected && !await TryConnectAsync(stoppingToken))
                {
                    await WaitForSignalOrTimeoutAsync(BrokerRetryInterval, stoppingToken);
                    continue;
                }

                await PublishAllAsync(stoppingToken);
                wait = TimeUntilNextBoundary();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT publish cycle failed; will retry");
                wait = BrokerRetryInterval;
            }

            await WaitForSignalOrTimeoutAsync(wait, stoppingToken);
        }

        if (_client.IsConnected)
        {
            await PublishAvailabilityAsync(online: false);
            await _client.DisconnectAsync(new MqttClientDisconnectOptions { Reason = MqttClientDisconnectOptionsReason.NormalDisconnection });
        }
    }

    private async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        var broker = await _brokerResolver.ResolveAsync(ct);
        if (broker is null)
        {
            _logger.LogWarning(
                "No MQTT broker configured or discoverable (checked manual override and Supervisor auto-discovery); publishing is disabled");
            return false;
        }

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId("wattsup")
            .WithTcpServer(broker.Host, broker.Port)
            .WithWillTopic(MqttDiscoveryPayloadBuilder.AvailabilityTopic)
            .WithWillPayload("offline")
            .WithWillRetain(true)
            .WithCleanSession();

        if (broker.Ssl)
        {
            optionsBuilder = optionsBuilder.WithTlsOptions(o => o.UseTls());
        }
        if (broker.Username is not null)
        {
            optionsBuilder = optionsBuilder.WithCredentials(broker.Username, broker.Password);
        }

        try
        {
            await _client.ConnectAsync(optionsBuilder.Build(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not connect to the MQTT broker at {Host}:{Port} (source: {Source}); will retry",
                broker.Host, broker.Port, broker.Source);
            return false;
        }

        _logger.LogInformation("Connected to MQTT broker at {Host}:{Port} (source: {Source})", broker.Host, broker.Port, broker.Source);

        // Force discovery configs to be republished after a (re)connect — a freshly (re)started
        // broker (e.g. the Mosquitto add-on was reinstalled) may have lost its retained messages.
        _discoveredPriceAreas.Clear();
        _diagnosticsDiscoveryPublished = false;
        await PublishAvailabilityAsync(online: true);
        return true;
    }

    private async Task PublishAllAsync(CancellationToken ct)
    {
        var settings = await _settingsRepository.GetAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var priceArea in settings.PriceAreas)
        {
            await EnsurePriceDiscoveryAsync(priceArea);

            var breakdown = await _priceCalculationService.CalculateAsync(priceArea, now, ct);
            await PublishAsync(MqttDiscoveryPayloadBuilder.PriceStateTopic(priceArea),
                breakdown.TotalDkkPerKwh.ToString("0.#####", CultureInfo.InvariantCulture), retain: false);
            await PublishAsync(MqttDiscoveryPayloadBuilder.PriceAttributesTopic(priceArea),
                MqttDiscoveryPayloadBuilder.Serialize(BuildAttributes(breakdown)), retain: false);
        }

        await EnsureDiagnosticsDiscoveryAsync();
        await PublishAsync(MqttDiscoveryPayloadBuilder.DiagnosticsStateTopic, "ok", retain: false);
        await PublishAsync(MqttDiscoveryPayloadBuilder.DiagnosticsAttributesTopic,
            MqttDiscoveryPayloadBuilder.Serialize(new
            {
                last_published_utc = now.ToString("O"),
                tracked_price_areas = settings.PriceAreas,
                grid_company = settings.GridCompanyName,
            }),
            retain: false);
    }

    private static object BuildAttributes(PriceBreakdown b) => new
    {
        spot_price_dkk_per_kwh = b.SpotPriceDkkPerKwh,
        grid_tariff_dkk_per_kwh = b.GridTariffDkkPerKwh,
        system_tariff_dkk_per_kwh = b.SystemTariffDkkPerKwh,
        transmission_tariff_dkk_per_kwh = b.TransmissionTariffDkkPerKwh,
        elafgift_dkk_per_kwh = b.ElafgiftDkkPerKwh,
        elafgift_rate_applied = b.ElafgiftReducedApplied ? "reduced" : "normal",
        markup_dkk_per_kwh = b.MarkupDkkPerKwh,
        vat_enabled = b.VatEnabled,
        subtotal_dkk_per_kwh = b.SubtotalDkkPerKwh,
        fully_resolved = b.FullyResolved,
        as_of_utc = b.AtUtc.ToString("O"),
    };

    private async Task EnsurePriceDiscoveryAsync(string priceArea)
    {
        if (!_discoveredPriceAreas.Add(priceArea))
        {
            return;
        }

        await PublishAsync(
            MqttDiscoveryPayloadBuilder.PriceDiscoveryTopic(priceArea),
            MqttDiscoveryPayloadBuilder.BuildPriceDiscoveryPayload(priceArea),
            retain: true);
    }

    private async Task EnsureDiagnosticsDiscoveryAsync()
    {
        if (_diagnosticsDiscoveryPublished)
        {
            return;
        }

        await PublishAsync(
            MqttDiscoveryPayloadBuilder.DiagnosticsDiscoveryTopic,
            MqttDiscoveryPayloadBuilder.BuildDiagnosticsDiscoveryPayload(),
            retain: true);
        _diagnosticsDiscoveryPublished = true;
    }

    private Task PublishAvailabilityAsync(bool online) =>
        PublishAsync(MqttDiscoveryPayloadBuilder.AvailabilityTopic, online ? "online" : "offline", retain: true);

    private async Task PublishAsync(string topic, string payload, bool retain)
    {
        if (!_client.IsConnected)
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message);
    }

    private static TimeSpan TimeUntilNextBoundary()
    {
        var now = DateTimeOffset.UtcNow;
        var minutesIntoBoundary = now.Minute % 15;
        var next = now
            .AddMinutes(-minutesIntoBoundary)
            .AddMinutes(15)
            .AddSeconds(-now.Second)
            .AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var wait = next - now;
        return wait <= TimeSpan.Zero ? BoundaryInterval : wait;
    }

    private async Task WaitForSignalOrTimeoutAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await _republishSignal.WaitAsync(timeout, ct);
            // Drain any extra pending signals so a burst of settings saves doesn't queue up N publishes.
            while (_republishSignal.CurrentCount > 0 && await _republishSignal.WaitAsync(0, ct))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
