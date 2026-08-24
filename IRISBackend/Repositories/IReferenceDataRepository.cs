using IRISBackend.Models;

namespace IRISBackend.Repositories
{
    public interface IReferenceDataRepository
    {
        Task<IReadOnlyList<Currency>> ListActiveCurrenciesAsync(CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<Counterparty> Items, int TotalCount)> SearchActiveCedentsAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default);

        Task<Counterparty?> GetActiveCedentByIdAsync(int counterpartyId, CancellationToken cancellationToken = default);

        Task<Currency?> GetActiveCurrencyByIdAsync(int currencyId, CancellationToken cancellationToken = default);
    }
}
