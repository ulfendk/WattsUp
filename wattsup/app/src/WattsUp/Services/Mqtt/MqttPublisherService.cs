using System.Globalization;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using WattsUp.Data.Repositories;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

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
    private static readonly int[] CheapestPeriodDurationsHours = [1, 2, 3, 4, 5, 6];

    private readonly IMqttBrokerResolver _brokerResolver;
    private readonly IPriceCalculationService _priceCalculationService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ISpotPriceRepository _spotPriceRepository;
    private readonly ILogger<MqttPublisherService> _logger;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _republishSignal = new(0, int.MaxValue);
    private readonly HashSet<string> _discoveredPriceAreas = [];
    private bool _diagnosticsDiscoveryPublished;
    private bool _cheapestPeriodDiscoveryPublished;

    public MqttPublisherService(
        IMqttBrokerResolver brokerResolver,
        IPriceCalculationService priceCalculationService,
        ISettingsRepository settingsRepository,
        ISpotPriceRepository spotPriceRepository,
        ILogger<MqttPublisherService> logger,
        Func<IMqttClient>? clientFactory = null)
    {
        _brokerResolver = brokerResolver;
        _priceCalculationService = priceCalculationService;
        _settingsRepository = settingsRepository;
        _spotPriceRepository = spotPriceRepository;
        _logger = logger;

        // clientFactory is only ever supplied by tests (a real IMqttClient can't be constructed
        // without MQTTnet's factory); DI falls back to this default when nothing is registered.
        _client = (clientFactory ?? (() => new MqttClientFactory().CreateMqttClient()))();
    }

    public void RequestRepublish() => _republishSignal.Release();

    public async Task UnpublishPriceAreaAsync(string priceArea, CancellationToken ct = default)
    {
        if (!_client.IsConnected && !await TryConnectAsync(ct))
        {
            return;
        }

        // HA MQTT Discovery convention: an empty retained payload on the config topic removes the entity.
        await PublishAsync(MqttDiscoveryPayloadBuilder.PriceDiscoveryTopic(priceArea), "", retain: true);
        _discoveredPriceAreas.Remove(priceArea);
    }

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
        _cheapestPeriodDiscoveryPublished = false;
        await PublishAvailabilityAsync(online: true);
        return true;
    }

    private async Task PublishAllAsync(CancellationToken ct)
    {
        var settings = await _settingsRepository.GetAsync(ct);
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(settings.PriceArea))
        {
            await EnsurePriceDiscoveryAsync(settings.PriceArea);

            var breakdown = await _priceCalculationService.CalculateAsync(settings.PriceArea, now, ct);
            await PublishAsync(MqttDiscoveryPayloadBuilder.PriceStateTopic(settings.PriceArea),
                breakdown.TotalDkkPerKwh.ToString("0.#####", CultureInfo.InvariantCulture), retain: false);
            await PublishAsync(MqttDiscoveryPayloadBuilder.PriceAttributesTopic(settings.PriceArea),
                MqttDiscoveryPayloadBuilder.Serialize(BuildAttributes(breakdown)), retain: false);

            await PublishCheapestPeriodsAsync(settings.PriceArea, now, ct);
        }

        await EnsureDiagnosticsDiscoveryAsync();
        await PublishAsync(MqttDiscoveryPayloadBuilder.DiagnosticsStateTopic, "ok", retain: false);
        await PublishAsync(MqttDiscoveryPayloadBuilder.DiagnosticsAttributesTopic,
            MqttDiscoveryPayloadBuilder.Serialize(new
            {
                last_published_utc = now.ToString("O"),
                tracked_price_area = settings.PriceArea,
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
        vat_amount_dkk_per_kwh = b.VatAmountDkkPerKwh,
        fully_resolved = b.FullyResolved,
        as_of_utc = b.AtUtc.ToString("O"),
    };

    /// <summary>Backlog item 7: publishes the cheapest 1–6 hour contiguous window's start time,
    /// over whatever hourly prices are already cached (today + published day-ahead prices).</summary>
    private async Task PublishCheapestPeriodsAsync(string priceArea, DateTimeOffset now, CancellationToken ct)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, TariffResolutionService.CopenhagenTimeZone);
        var localMidnight = new DateTimeOffset(localNow.Date, localNow.Offset);
        var fromUtc = localMidnight.ToUniversalTime();
        var toUtc = fromUtc.AddDays(2);

        var spotPrices = await _spotPriceRepository.GetRangeAsync(priceArea, fromUtc, toUtc, ct);
        var hourlyPrices = new List<(DateTimeOffset AtUtc, decimal TotalDkkPerKwh)>(spotPrices.Count);
        foreach (var spot in spotPrices)
        {
            var breakdown = await _priceCalculationService.CalculateAsync(priceArea, spot.TimeUtc, ct);
            hourlyPrices.Add((spot.TimeUtc, breakdown.TotalDkkPerKwh));
        }

        var results = CheapestPeriodCalculator.FindCheapestPeriods(hourlyPrices, CheapestPeriodDurationsHours);

        await EnsureCheapestPeriodDiscoveryAsync();
        foreach (var result in results)
        {
            await PublishAsync(
                MqttDiscoveryPayloadBuilder.CheapestPeriodStateTopic(result.DurationHours),
                result.StartAtUtc.ToString("O"),
                retain: false);
            await PublishAsync(
                MqttDiscoveryPayloadBuilder.CheapestPeriodAttributesTopic(result.DurationHours),
                MqttDiscoveryPayloadBuilder.Serialize(new
                {
                    average_price_dkk_per_kwh = result.AveragePriceDkkPerKwh,
                    duration_hours = result.DurationHours,
                    ends_at_utc = result.StartAtUtc.AddHours(result.DurationHours).ToString("O"),
                }),
                retain: false);
        }
    }

    private async Task EnsureCheapestPeriodDiscoveryAsync()
    {
        if (_cheapestPeriodDiscoveryPublished)
        {
            return;
        }

        foreach (var hours in CheapestPeriodDurationsHours)
        {
            await PublishAsync(
                MqttDiscoveryPayloadBuilder.CheapestPeriodDiscoveryTopic(hours),
                MqttDiscoveryPayloadBuilder.BuildCheapestPeriodDiscoveryPayload(hours),
                retain: true);
        }
        _cheapestPeriodDiscoveryPublished = true;
    }

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
