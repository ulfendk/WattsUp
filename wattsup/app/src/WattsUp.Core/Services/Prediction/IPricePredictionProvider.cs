namespace WattsUp.Services.Prediction;

/// <summary>
/// Extension seam for price predictions (e.g. Carnot.dk's AI-based 7-day DK1/DK2 forecasts).
/// Deliberately unimplemented this iteration — no registered implementation exists yet, this
/// interface only exists so the architecture leaves room for one without a later breaking change.
/// </summary>
public interface IPricePredictionProvider
{
    Task<IReadOnlyList<PricePrediction>> GetPredictionsAsync(
        string priceArea, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}

public sealed record PricePrediction(DateTimeOffset AtUtc, decimal PredictedPriceDkkPerKwh, decimal? ConfidenceLow, decimal? ConfidenceHigh);
