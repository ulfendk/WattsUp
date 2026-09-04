using WattsUp.Data.Repositories;
using WattsUp.Services.Homeassistant;

namespace WattsUp.BackgroundServices;

/// <summary>
/// Polls each selected HA consumption device's current state (via the Home Assistant REST API, not
/// MQTT — see <see cref="HomeAssistantApiClient"/>) on the same 15-minute cadence as
/// <c>MqttPublisherService</c>'s settlement boundary, and accumulates it into
/// <c>device_hourly_consumption</c>. Backlog item 4.
///
/// Prefers <c>device_class: energy</c> entities (cumulative kWh/Wh — diffed between polls, exact)
/// over <c>device_class: power</c> entities (instantaneous W/kW — trapezoidal-integrated over the
/// elapsed time since the last poll, an approximation) when a device only exposes the latter.
/// </summary>
public sealed class DeviceConsumptionPollingService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeviceConsumptionPollingService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    // In-process only: entity_id -> (last poll time, last raw HA state value). Reset on restart —
    // the first poll after a restart establishes a fresh baseline rather than guessing at a delta.
    private readonly Dictionary<string, (DateTimeOffset PolledAt, decimal Value)> _lastReadings = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var haClient = scope.ServiceProvider.GetRequiredService<IHomeAssistantApiClient>();
        if (!haClient.IsAvailable)
        {
            return;
        }

        var deviceRepository = scope.ServiceProvider.GetRequiredService<IConsumptionDeviceRepository>();
        var consumptionRepository = scope.ServiceProvider.GetRequiredService<IDeviceHourlyConsumptionRepository>();

        var devices = await deviceRepository.GetSelectedAsync(ct);
        if (devices.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var hourStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

        foreach (var device in devices)
        {
            try
            {
                var value = await haClient.GetStateAsync(device.EntityId, ct);
                if (value is null)
                {
                    continue;
                }

                var addedKwh = ComputeAddedKwh(device, value.Value, now);
                if (addedKwh is null)
                {
                    continue; // first sample seen for this device this run — baseline only, no delta yet
                }

                var latest = await consumptionRepository.GetLatestAsync(device.EntityId, ct);
                var currentHourKwh = latest is not null && latest.HourUtc == hourStart ? latest.Kwh : 0m;
                await consumptionRepository.UpsertAsync(
                    new DeviceHourlyConsumption(device.EntityId, hourStart, currentHourKwh + addedKwh.Value), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to poll consumption for {EntityId}", device.EntityId);
            }
        }
    }

    private decimal? ComputeAddedKwh(ConsumptionDevice device, decimal rawValue, DateTimeOffset now)
    {
        var hadLast = _lastReadings.TryGetValue(device.EntityId, out var last);
        _lastReadings[device.EntityId] = (now, rawValue);
        if (!hadLast)
        {
            return null;
        }

        // W/Wh readings need /1000 to reach the kW/kWh units the rest of the app works in.
        var unitScale = device.UnitOfMeasure is "W" or "Wh" ? 0.001m : 1m;
        var isEnergy = string.Equals(device.DeviceClass, "energy", StringComparison.OrdinalIgnoreCase);

        if (isEnergy)
        {
            // Cumulative counter: diff, guarding against a meter/HA-restart reset (value went backwards).
            var deltaKwh = (rawValue - last.Value) * unitScale;
            return deltaKwh >= 0 ? deltaKwh : 0m;
        }

        var elapsedHours = (decimal)(now - last.PolledAt).TotalHours;
        if (elapsedHours <= 0)
        {
            return 0m;
        }

        var avgKw = (rawValue + last.Value) / 2m * unitScale;
        return avgKw * elapsedHours;
    }
}
