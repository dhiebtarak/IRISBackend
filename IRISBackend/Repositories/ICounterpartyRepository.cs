using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Repositories
{
    public interface ICounterpartyRepository : IGenericRepository<Counterparty>
    {
        Task<bool> LegacyClientIdExistsAsync(int legacyClientId, int? excludedId = null, CancellationToken cancellationToken = default);

        Task<bool> LegacyCompanyCodeExistsAsync(string legacyCompanyCode, int? excludedId = null, CancellationToken cancellationToken = default);

        Task<(IEnumerable<Counterparty> Items, int TotalCount)> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default);
    }
}
