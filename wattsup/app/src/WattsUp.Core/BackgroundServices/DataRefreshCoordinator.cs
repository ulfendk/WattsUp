namespace WattsUp.BackgroundServices;

/// <summary>
/// Lets the UI trigger the three data pollers on demand instead of waiting for their normal
/// hourly/daily schedule. Primarily for the WASM build's manual "Refresh" button — the public
/// demo has no server pushing updates to a visitor, so it needs an explicit way to ask for
/// today's freshest prices/tariffs/consumption right now. Registered (and harmless to call) on
/// the Server/add-on host too, for parity — see Program.cs in both hosts.
/// </summary>
public interface IDataRefreshCoordinator
{
    /// <summary>Raised after a manual refresh completes (success or failure on any individual
    /// poller doesn't stop the others — each already logs/reports its own failure). Pages showing
    /// cached data (Dashboard, Diagnostics) subscribe to this to re-read and re-render.</summary>
    event Action? Refreshed;

    Task RefreshNowAsync(CancellationToken ct = default);
}

public sealed class DataRefreshCoordinator(
    SpotPricePollingService spotPricePoller,
    TariffPollingService tariffPoller,
    EloverblikConsumptionPollingService consumptionPoller) : IDataRefreshCoordinator
{
    public event Action? Refreshed;

    public async Task RefreshNowAsync(CancellationToken ct = default)
    {
        // Sequential, not parallel: TariffPollingService reads settings written by nothing here,
        // but running all three against the same IndexedDB/SQLite connection pool concurrently is
        // needless contention for what is, at most, an every-few-minutes manual click.
        await spotPricePoller.PollOnceAsync(ct);
        await tariffPoller.PollOnceAsync(ct);
        await consumptionPoller.PollOnceAsync(ct);
        Refreshed?.Invoke();
    }
}
