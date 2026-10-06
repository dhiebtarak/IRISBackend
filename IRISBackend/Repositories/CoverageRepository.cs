using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Repositories
{
    public sealed class CoverageRepository : ICoverageRepository
    {
        private readonly IRISBackendContext _dbContext;

        public CoverageRepository(IRISBackendContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<CoverageCatalogItemDto>> ListMainCoversAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.CoverageItems
                .AsNoTracking()
                .Where(item =>
                    item.IsActive &&
                    (item.CoverageLevel.Trim().ToUpper() == "MAIN" ||
                     item.CoverageLevel.Trim().ToUpper() == "MAIN_COVER"))
                .Select(item => new
                {
                    item.DisplayOrder,
                    item.CoverageName,
                    Dto = new CoverageCatalogItemDto
                    {
                        Id = item.IdCoverageItem,
                        Code = item.CoverageCode,
                        Name = item.CoverageName,
                        Level = item.CoverageLevel,
                        ParentId = null,
                        RelationType = null,
                        IsMandatory = false,
                        HasSlip = _dbContext.SlipTemplates.Any(slip =>
                            slip.IdCoverageItem == item.IdCoverageItem &&
                            slip.IsActive &&
                            slip.IsCurrent)
                    }
                })
                .OrderBy(item => item.DisplayOrder ?? int.MaxValue)
                .ThenBy(item => item.CoverageName)
                .Select(item => item.Dto)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CoverageCatalogItemDto>> ListChildrenAsync(
            int parentCoverageItemId,
            string requestedLevel,
            string relationType,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.CoverageRelations
                .AsNoTracking()
                .Where(relation =>
                    relation.IsActive &&
                    relation.IdParentCoverageItem == parentCoverageItemId &&
                    ((relationType == CoverageConstants.HasSectionRelation &&
                      (relation.RelationType.Trim().ToUpper() == CoverageConstants.HasSectionRelation ||
                       relation.RelationType.Trim().ToUpper() == CoverageConstants.SectionLevel)) ||
                     (relationType == CoverageConstants.HasExtensionRelation &&
                      (relation.RelationType.Trim().ToUpper() == CoverageConstants.HasExtensionRelation ||
                       relation.RelationType.Trim().ToUpper() == CoverageConstants.ExtensionLevel))) &&
                    relation.IdParentCoverageItemNavigation.IsActive &&
                    relation.IdChildCoverageItemNavigation.IsActive &&
                    relation.IdChildCoverageItemNavigation.CoverageLevel.Trim().ToUpper() == requestedLevel.Trim().ToUpper())
                .Select(relation => new
                {
                    relation.DisplayOrder,
                    ChildDisplayOrder = relation.IdChildCoverageItemNavigation.DisplayOrder,
                    ChildName = relation.IdChildCoverageItemNavigation.CoverageName,
                    Dto = new CoverageCatalogItemDto
                    {
                        Id = relation.IdChildCoverageItem,
                        Code = relation.IdChildCoverageItemNavigation.CoverageCode,
                        Name = relation.IdChildCoverageItemNavigation.CoverageName,
                        Level = relation.IdChildCoverageItemNavigation.CoverageLevel,
                        ParentId = relation.IdParentCoverageItem,
                        RelationType = relation.RelationType,
                        IsMandatory = relation.IsMandatory,
                        HasSlip = _dbContext.SlipTemplates.Any(slip =>
                            slip.IdCoverageItem == relation.IdChildCoverageItem &&
                            slip.IsActive &&
                            slip.IsCurrent)
                    }
                })
                .OrderBy(item => item.DisplayOrder ?? item.ChildDisplayOrder ?? int.MaxValue)
                .ThenBy(item => item.ChildName)
                .Select(item => item.Dto)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CoverageItemLookup>> GetCoverageItemsByIdsAsync(
            IReadOnlyCollection<int> coverageItemIds,
            CancellationToken cancellationToken = default)
        {
            if (coverageItemIds.Count == 0)
            {
                return [];
            }

            return await _dbContext.CoverageItems
                .AsNoTracking()
                .Where(item => coverageItemIds.Contains(item.IdCoverageItem))
                .Select(item => new CoverageItemLookup(
                    item.IdCoverageItem,
                    item.CoverageLevel,
                    item.CoverageCode,
                    item.CoverageName,
                    item.IsActive))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CoverageRelationLookup>> GetActiveRelationsForParentsAsync(
            IReadOnlyCollection<int> parentCoverageItemIds,
            CancellationToken cancellationToken = default)
        {
            if (parentCoverageItemIds.Count == 0)
            {
                return [];
            }

            return await _dbContext.CoverageRelations
                .AsNoTracking()
                .Where(relation =>
                    parentCoverageItemIds.Contains(relation.IdParentCoverageItem) &&
                    relation.IsActive)
                .Select(relation => new CoverageRelationLookup(
                    relation.IdParentCoverageItem,
                    relation.IdChildCoverageItem,
                    relation.RelationType,
                    relation.IsMandatory,
                    relation.IsActive))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<int, int>> GetActiveCurrentSlipIdsAsync(
            IReadOnlyCollection<int> coverageItemIds,
            CancellationToken cancellationToken = default)
        {
            if (coverageItemIds.Count == 0)
            {
                return new Dictionary<int, int>();
            }

            return await _dbContext.SlipTemplates
                .AsNoTracking()
                .Where(slip =>
                    coverageItemIds.Contains(slip.IdCoverageItem) &&
                    slip.IsActive &&
                    slip.IsCurrent)
                .Select(slip => new { slip.IdCoverageItem, slip.IdSlipTemplate })
                .ToDictionaryAsync(slip => slip.IdCoverageItem, slip => slip.IdSlipTemplate, cancellationToken);
        }

        public async Task<IReadOnlyList<ContractCoverageSelection>> GetSelectionsForContractAsync(
            int contractId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.ContractCoverageSelections
                .Include(selection => selection.IdCoverageItemNavigation)
                .Where(selection => selection.IdContract == contractId)
                .ToListAsync(cancellationToken);
        }

        public Task AddSelectionsAsync(
            IEnumerable<ContractCoverageSelection> selections,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.ContractCoverageSelections.AddRangeAsync(selections, cancellationToken);
        }

        public void RemoveSelections(IEnumerable<ContractCoverageSelection> selections)
        {
            _dbContext.ContractCoverageSelections.RemoveRange(selections);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
