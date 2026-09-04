namespace WattsUp.Services.Mqtt;

/// <summary>Lets other services (Settings save handler, pollers) ask for an immediate republish.</summary>
public interface IMqttPublisherService
{
    void RequestRepublish();

    /// <summary>Removes a price area's HA MQTT Discovery entity (backlog item 5 — switching the
    /// tracked region must clear the old one's sensor, not just stop updating it).</summary>
    Task UnpublishPriceAreaAsync(string priceArea, CancellationToken ct = default);
}
