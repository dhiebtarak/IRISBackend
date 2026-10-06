using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface IContractReadService
    {
        Task<ServiceResult<PagedResponseDto<ContractListItemDto>>> ListAsync(
            ContractListQueryDto query,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractDashboardSummaryDto>> GetDashboardSummaryAsync(
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractOverviewDto>> GetOverviewAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractOverviewDto>> CompleteDraftAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<SlipDownloadResult>> DownloadSlipsAsync(
            int contractId,
            CancellationToken cancellationToken = default);
    }
}
