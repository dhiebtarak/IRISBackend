using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface ICoverageCatalogService
    {
        Task<ServiceResult<IEnumerable<CoverageCatalogItemDto>>> GetMainCoversAsync(CancellationToken cancellationToken = default);

        Task<ServiceResult<IEnumerable<CoverageCatalogItemDto>>> GetChildrenAsync(
            int parentCoverageItemId,
            string? level,
            CancellationToken cancellationToken = default);
    }
}
