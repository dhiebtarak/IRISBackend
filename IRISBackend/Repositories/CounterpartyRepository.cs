using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
using IRISBackend.Services;

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

        public Task<Counterparty?> GetByIdWithTypesAsync(int id, CancellationToken cancellationToken = default)
        {
            return _dbContext.Counterparties
                .Include(counterparty => counterparty.TypeAssignments)
                .SingleOrDefaultAsync(counterparty => counterparty.Id == id, cancellationToken);
        }

        public async Task ReplaceTypeAssignmentsAsync(
            Counterparty counterparty,
            IReadOnlyCollection<string> types,
            CancellationToken cancellationToken = default)
        {
            if (counterparty.Id == 0)
            {
                counterparty.TypeAssignments = types
                    .Select(type => new CounterpartyTypeAssignment
                    {
                        Type = type,
                        Counterparty = counterparty
                    })
                    .ToList();
                return;
            }

            var existing = await _dbContext.CounterpartyTypeAssignments
                .Where(assignment => assignment.CounterpartyId == counterparty.Id)
                .ToListAsync(cancellationToken);

            _dbContext.CounterpartyTypeAssignments.RemoveRange(existing);
            var assignments = types
                .Select(type => new CounterpartyTypeAssignment
                {
                    CounterpartyId = counterparty.Id,
                    Type = type,
                    Counterparty = counterparty
                })
                .ToList();

            counterparty.TypeAssignments = assignments;
            await _dbContext.CounterpartyTypeAssignments.AddRangeAsync(assignments, cancellationToken);
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

        public async Task<(IReadOnlyList<CounterpartyLookupItemDto> Items, int TotalCount)> SearchActiveReinsurersAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
            var query = _dbContext.Counterparties
                .AsNoTracking()
                .Where(counterparty =>
                    counterparty.IsActive &&
                    counterparty.TypeAssignments.Any(assignment => assignment.Type == CounterpartyTypes.Reinsurer));

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(counterparty =>
                    (counterparty.Name != null && counterparty.Name.ToLower().Contains(normalizedSearch)) ||
                    (counterparty.Country != null && counterparty.Country.ToLower().Contains(normalizedSearch)) ||
                    (counterparty.City != null && counterparty.City.ToLower().Contains(normalizedSearch)));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderBy(counterparty => counterparty.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(counterparty => new CounterpartyLookupItemDto
                {
                    Id = counterparty.Id,
                    Name = counterparty.Name,
                    Types = counterparty.TypeAssignments
                        .OrderBy(assignment => assignment.Type)
                        .Select(assignment => assignment.Type)
                        .ToArray(),
                    Country = counterparty.Country,
                    City = counterparty.City
                })
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        private static string Normalize(string value)
        {
            return value.Trim().ToUpperInvariant();
        }
    }
}
