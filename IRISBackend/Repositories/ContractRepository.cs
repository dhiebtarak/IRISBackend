using IRISBackend.Models;
using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories
{
    public sealed class ContractRepository : IContractRepository
    {
        private readonly IRISBackendContext _dbContext;

        public ContractRepository(IRISBackendContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default)
        {
            var normalizedUmr = umr.Trim().ToUpperInvariant();

            return _dbContext.Contracts.AnyAsync(
                contract =>
                    contract.Umr != null &&
                    contract.Umr.ToUpper() == normalizedUmr &&
                    (!excludedContractId.HasValue || contract.IdContract != excludedContractId.Value),
                cancellationToken);
        }

        public Task AddAsync(Contract contract, CancellationToken cancellationToken = default)
        {
            return _dbContext.Contracts.AddAsync(contract, cancellationToken).AsTask();
        }

        public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default)
        {
            return _dbContext.ContractFacs.AddAsync(contractFac, cancellationToken).AsTask();
        }

        public void Update(Contract contract)
        {
            _dbContext.Contracts.Update(contract);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task<Contract?> GetFacultativeContractByIdAsync(
            int contractId,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            IQueryable<Contract> query = _dbContext.Contracts
                .Include(contract => contract.ContractFac)
                .Include(contract => contract.ContractTreatyProp)
                    .ThenInclude(treaty => treaty.ContractTreatyPropQuotaShare)
                .Include(contract => contract.ContractTreatyProp)
                    .ThenInclude(treaty => treaty.ContractTreatyPropSurplu)
                .Include(contract => contract.ContractTreatyNonprops)
                .Include(contract => contract.IdCounterpartyNavigation)
                .Include(contract => contract.IdCurrencyNavigation)
                .Include(contract => contract.CreatedByNavigation)
                .Include(contract => contract.UpdatedByNavigation)
                .Include(contract => contract.ContractCoverageSelections)
                    .ThenInclude(selection => selection.IdCoverageItemNavigation)
                .Where(contract =>
                    contract.IdContract == contractId &&
                    contract.TypeOfBusiness == ContractConstants.FacultativeType);

            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return query.SingleOrDefaultAsync(cancellationToken);
        }

        public Task<Contract?> GetAccessibleContractByIdAsync(
            int contractId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.Contracts
                .AsNoTracking()
                .Include(contract => contract.ContractFac)
                .Include(contract => contract.ContractTreatyProp)
                    .ThenInclude(treaty => treaty.ContractTreatyPropQuotaShare)
                .Include(contract => contract.ContractTreatyProp)
                    .ThenInclude(treaty => treaty.ContractTreatyPropSurplu)
                .Include(contract => contract.ContractTreatyNonprops)
                .Include(contract => contract.IdCounterpartyNavigation)
                .Include(contract => contract.IdCurrencyNavigation)
                .Include(contract => contract.CreatedByNavigation)
                .Include(contract => contract.UpdatedByNavigation)
                .Include(contract => contract.ContractPanels)
                    .ThenInclude(panel => panel.IdCounterpartyNavigation)
                .Include(contract => contract.ContractCoverageSelections)
                    .ThenInclude(selection => selection.IdCoverageItemNavigation)
                .Where(contract =>
                    contract.IdContract == contractId &&
                    contract.IsCurrent &&
                    contract.IdUser == userId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        public Task MarkDraftReadyForPlacementAsync(
            int contractId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.Contracts
                .Where(contract =>
                    contract.IdContract == contractId &&
                    contract.IdUser == userId &&
                    contract.Status == ContractConstants.DraftStatus)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(contract => contract.Status, ContractConstants.ReadyForPlacementStatus)
                    .SetProperty(contract => contract.UpdatedAt, DateTime.UtcNow)
                    .SetProperty(contract => contract.UpdatedBy, userId), cancellationToken);
        }

        public async Task<IReadOnlyList<SlipTemplate>> ListActiveCurrentSlipTemplatesForContractAsync(
            int contractId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ContractCoverageSelections
                .AsNoTracking()
                .Where(selection =>
                    selection.IdContract == contractId &&
                    selection.IdContractNavigation.IsCurrent &&
                    selection.IdContractNavigation.IdUser == userId)
                .SelectMany(selection => _dbContext.SlipTemplates
                    .Where(slip =>
                        slip.IdCoverageItem == selection.IdCoverageItem &&
                        slip.IsActive &&
                        slip.IsCurrent))
                .GroupBy(slip => slip.IdSlipTemplate)
                .Select(group => group.First())
                .OrderBy(slip => slip.IdCoverageItemNavigation.DisplayOrder ?? int.MaxValue)
                .ThenBy(slip => slip.IdCoverageItemNavigation.CoverageName)
                .ToListAsync(cancellationToken);
        }

        public async Task<(IReadOnlyList<ContractListItemDto> Items, int TotalCount)> ListAsync(
            ContractListQueryDto query,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var contracts = AccessibleContracts(userId);
            var filteredContracts = ContractQueryBuilder.ApplyFilters(contracts, query);
            var totalCount = await filteredContracts.CountAsync(cancellationToken);
            var projectedContracts = ContractQueryBuilder.ProjectListItems(filteredContracts);
            var items = await ContractQueryBuilder.ApplySorting(projectedContracts, query)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<ContractDashboardData> GetDashboardDataAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            var contracts = AccessibleContracts(userId);
            var totalContracts = await contracts.CountAsync(cancellationToken);
            var actionRequiredContracts = await contracts.CountAsync(contract => contract.Flag, cancellationToken);
            var statusCounts = await contracts
                .GroupBy(contract => contract.Status)
                .Select(group => new ContractStatusCount
                {
                    Status = group.Key,
                    Count = group.Count()
                })
                .OrderBy(item => item.Status)
                .ToListAsync(cancellationToken);

            var recentContracts = await ContractQueryBuilder.ProjectRecentContracts(
                    contracts.OrderByDescending(contract => contract.UpdatedAt ?? contract.CreatedAt))
                .Take(5)
                .ToListAsync(cancellationToken);

            return new ContractDashboardData
            {
                TotalContracts = totalContracts,
                ActionRequiredContracts = actionRequiredContracts,
                StatusCounts = statusCounts,
                RecentContracts = recentContracts
            };
        }

        private IQueryable<Contract> AccessibleContracts(string userId)
        {
            return _dbContext.Contracts
                .AsNoTracking()
                .Where(contract => contract.IsCurrent && contract.IdUser == userId);
        }
    }
}
