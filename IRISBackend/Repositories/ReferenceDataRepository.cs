using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Repositories
{
    public sealed class ReferenceDataRepository : IReferenceDataRepository
    {
        private const string CedentCounterpartyType = "cedent";

        private readonly IRISBackendContext _dbContext;

        public ReferenceDataRepository(IRISBackendContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<Currency>> ListActiveCurrenciesAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Currencies
                .AsNoTracking()
                .Where(currency => currency.IsActive)
                .OrderByDescending(currency => currency.IsDefault)
                .ThenBy(currency => currency.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<(IReadOnlyList<Counterparty> Items, int TotalCount)> SearchActiveCedentsAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var normalizedSearch = NormalizeOptionalSearch(search);
            var query = _dbContext.Counterparties
                .AsNoTracking()
                .Where(IsEligibleCedent());

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(counterparty =>
                    counterparty.Name != null &&
                    counterparty.Name.ToLower().Contains(normalizedSearch));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderBy(counterparty => counterparty.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public Task<Counterparty?> GetActiveCedentByIdAsync(int counterpartyId, CancellationToken cancellationToken = default)
        {
            return _dbContext.Counterparties
                .AsNoTracking()
                .FirstOrDefaultAsync(counterparty =>
                    counterparty.Id == counterpartyId &&
                    counterparty.IsActive &&
                    counterparty.Type != null &&
                    counterparty.Type.Trim().ToLower() == CedentCounterpartyType,
                    cancellationToken);
        }

        public Task<Currency?> GetActiveCurrencyByIdAsync(int currencyId, CancellationToken cancellationToken = default)
        {
            return _dbContext.Currencies
                .AsNoTracking()
                .FirstOrDefaultAsync(currency => currency.IdCurrency == currencyId && currency.IsActive, cancellationToken);
        }

        private static string? NormalizeOptionalSearch(string? search)
        {
            return string.IsNullOrWhiteSpace(search)
                ? null
                : search.Trim().ToLowerInvariant();
        }

        private static System.Linq.Expressions.Expression<Func<Counterparty, bool>> IsEligibleCedent()
        {
            return counterparty =>
                counterparty.IsActive &&
                counterparty.Type != null &&
                counterparty.Type.Trim().ToLower() == CedentCounterpartyType;
        }
    }
}
