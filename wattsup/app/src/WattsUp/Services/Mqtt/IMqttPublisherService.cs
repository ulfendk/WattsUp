namespace WattsUp.Services.Mqtt;

/// <summary>Lets other services (Settings save handler, pollers) ask for an immediate republish.</summary>
public interface IMqttPublisherService
{
    void RequestRepublish();
}
