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

        public async Task<(IEnumerable<Counterparty> Items, int TotalCount)> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default)
        {
            var pageNumber = Math.Max(query.PageNumber, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var counterparties = _dbContext.Counterparties.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                counterparties = counterparties.Where(counterparty =>
                    (counterparty.LegacyClientId.HasValue && counterparty.LegacyClientId.Value.ToString().Contains(search)) ||
                    (counterparty.LegacyCompanyCode != null && counterparty.LegacyCompanyCode.Contains(search)) ||
                    counterparty.Name.Contains(search) ||
                    (counterparty.Email != null && counterparty.Email.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(query.Type))
            {
                var type = query.Type.Trim();
                counterparties = counterparties.Where(counterparty => counterparty.Type == type);
            }

            if (query.IsActive.HasValue)
            {
                counterparties = counterparties.Where(counterparty => counterparty.IsActive == query.IsActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var status = query.Status.Trim();
                counterparties = counterparties.Where(counterparty => counterparty.Status == status);
            }

            var totalCount = await counterparties.CountAsync(cancellationToken);
            var items = await counterparties
                .OrderBy(counterparty => counterparty.Name)
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
