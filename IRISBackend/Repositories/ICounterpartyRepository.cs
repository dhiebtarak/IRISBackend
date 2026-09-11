using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Repositories
{
    public interface ICounterpartyRepository : IGenericRepository<Counterparty>
    {
        Task<bool> LegacyClientIdExistsAsync(int legacyClientId, int? excludedId = null, CancellationToken cancellationToken = default);

        Task<bool> LegacyCompanyCodeExistsAsync(string legacyCompanyCode, int? excludedId = null, CancellationToken cancellationToken = default);

        Task<Counterparty?> GetByIdWithTypesAsync(int id, CancellationToken cancellationToken = default);

        Task ReplaceTypeAssignmentsAsync(
            Counterparty counterparty,
            IReadOnlyCollection<string> types,
            CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<CounterpartyListItemDto> Items, int TotalCount)> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default);

        Task<(IReadOnlyList<CounterpartyLookupItemDto> Items, int TotalCount)> SearchActiveReinsurersAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default);
    }
}
