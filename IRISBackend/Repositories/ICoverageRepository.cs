using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Repositories
{
    public interface ICoverageRepository
    {
        Task<IReadOnlyList<CoverageCatalogItemDto>> ListMainCoversAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CoverageCatalogItemDto>> ListChildrenAsync(
            int parentCoverageItemId,
            string requestedLevel,
            string relationType,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CoverageItemLookup>> GetCoverageItemsByIdsAsync(
            IReadOnlyCollection<int> coverageItemIds,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CoverageRelationLookup>> GetActiveRelationsForParentsAsync(
            IReadOnlyCollection<int> parentCoverageItemIds,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<int, int>> GetActiveCurrentSlipIdsAsync(
            IReadOnlyCollection<int> coverageItemIds,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ContractCoverageSelection>> GetSelectionsForContractAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task AddSelectionsAsync(
            IEnumerable<ContractCoverageSelection> selections,
            CancellationToken cancellationToken = default);

        void RemoveSelections(IEnumerable<ContractCoverageSelection> selections);

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
