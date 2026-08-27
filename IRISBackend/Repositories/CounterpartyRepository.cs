using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Repositories
{
    public class CounterpartyRepository : GenericRepository<Counterparty>, ICounterpartyRepository
    {
        private readonly IRISBackendContext _dbContext;
            
        public CounterpartyRepository(IRISBackendContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<bool> LegacyClientIdExistsAsync(int legacyClientId, int? excludedId = null, CancellationToken cancellationToken = default)
        {
            return _dbContext.Counterparties
                .AnyAsync(
                    counterparty =>
                        counterparty.LegacyClientId == legacyClientId &&
                        (!excludedId.HasValue || counterparty.Id != excludedId.Value),
                    cancellationToken);
        }

        public Task<bool> LegacyCompanyCodeExistsAsync(string legacyCompanyCode, int? excludedId = null, CancellationToken cancellationToken = default)
        {
            var normalizedCode = Normalize(legacyCompanyCode);

            return _dbContext.Counterparties
                .AnyAsync(
                    counterparty =>
                        counterparty.LegacyCompanyCode != null &&
                        counterparty.LegacyCompanyCode.ToUpper() == normalizedCode &&
                        (!excludedId.HasValue || counterparty.Id != excludedId.Value),
                    cancellationToken);
        }

        public async Task<(IReadOnlyList<CounterpartyListItemDto> Items, int TotalCount)> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default)
        {
            var pageNumber = Math.Max(query.PageNumber, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var counterparties = CounterpartyQueryBuilder.ApplyFilters(
                _dbContext.Counterparties.AsNoTracking(),
                query);

            var totalCount = await counterparties.CountAsync(cancellationToken);
            var sortedCounterparties = CounterpartyQueryBuilder.ApplySorting(counterparties, query);
            var items = await CounterpartyQueryBuilder.ProjectListItems(sortedCounterparties)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        private static string Normalize(string value)
        {
            return value.Trim().ToUpperInvariant();
        }
    }
}
