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
    }
}
