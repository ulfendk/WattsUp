namespace WattsUp.Services.Diagnostics;

public sealed record PollStatus(DateTimeOffset? LastSuccessUtc, DateTimeOffset? LastAttemptUtc, string? LastError);

/// <summary>
/// In-memory, process-lifetime status board for the background pollers and MQTT publisher.
/// Read by the Diagnostics page and published as MQTT diagnostics attributes — never persisted,
/// it exists purely to answer "is the data I'm looking at stale, and why".
/// </summary>
public sealed class DiagnosticsStatusService
{
    private readonly Lock _lock = new();
    private readonly List<string> _warnings = [];

    private bool _spotPriceFetching;
    private PollStatus _spotPricePoll = new(null, null, null);
    private PollStatus _tariffPoll = new(null, null, null);
    private PollStatus _consumptionPoll = new(null, null, null);

    public PollStatus SpotPricePoll { get { lock (_lock) { return _spotPricePoll; } } }
    public PollStatus TariffPoll { get { lock (_lock) { return _tariffPoll; } } }
    public PollStatus ConsumptionPoll { get { lock (_lock) { return _consumptionPoll; } } }

    /// <summary>True while a spot price poll is in flight. The dashboard uses this to show a
    /// "fetching" indicator when it has no current data to display yet.</summary>
    public bool SpotPriceFetching { get { lock (_lock) { return _spotPriceFetching; } } }

    /// <summary>Raised when <see cref="SpotPriceFetching"/> flips, on whatever thread flipped it.</summary>
    public event Action? SpotPriceFetchingChanged;

    public void SetSpotPriceFetching(bool fetching)
    {
        lock (_lock) { _spotPriceFetching = fetching; }
        SpotPriceFetchingChanged?.Invoke();
    }

    public IReadOnlyList<string> Warnings { get { lock (_lock) { return _warnings.ToList(); } } }

    public void ReportSpotPriceSuccess() { lock (_lock) { _spotPricePoll = new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null); } }
    public void ReportSpotPriceFailure(string error) { lock (_lock) { _spotPricePoll = _spotPricePoll with { LastAttemptUtc = DateTimeOffset.UtcNow, LastError = error }; } }

    public void ReportTariffSuccess() { lock (_lock) { _tariffPoll = new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null); } }
    public void ReportTariffFailure(string error) { lock (_lock) { _tariffPoll = _tariffPoll with { LastAttemptUtc = DateTimeOffset.UtcNow, LastError = error }; } }

    public void ReportConsumptionSuccess() { lock (_lock) { _consumptionPoll = new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null); } }
    public void ReportConsumptionFailure(string error) { lock (_lock) { _consumptionPoll = _consumptionPoll with { LastAttemptUtc = DateTimeOffset.UtcNow, LastError = error }; } }

    public void AddWarning(string warning)
    {
        lock (_lock)
        {
            _warnings.RemoveAll(w => w == warning);
            _warnings.Insert(0, warning);
            if (_warnings.Count > 20)
            {
                _warnings.RemoveRange(20, _warnings.Count - 20);
            }
        }
    }
}
