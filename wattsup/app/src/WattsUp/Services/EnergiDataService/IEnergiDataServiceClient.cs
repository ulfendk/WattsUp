using WattsUp.Services.EnergiDataService.Dto;

namespace WattsUp.Services.EnergiDataService;

public interface IEnergiDataServiceClient
{
    /// <summary>Fetches DayAheadPrices for the given price areas within [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<DayAheadPriceRecord>> GetDayAheadPricesAsync(
        IReadOnlyCollection<string> priceAreas, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Fetches all currently-relevant DatahubPricelist rows for a single GLN number.</summary>
    Task<IReadOnlyList<DatahubPricelistRecord>> GetTariffLineItemsAsync(string glnNumber, CancellationToken ct = default);

    /// <summary>Distinct (GLN, ChargeOwner) pairs offering grid-tariff ("Nettarif") charges, for the grid-company picker.</summary>
    Task<IReadOnlyList<(string GlnNumber, string ChargeOwner)>> GetDistinctGridCompaniesAsync(CancellationToken ct = default);
}
