using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface IContractPlacementService
    {
        Task<ServiceResult<IReadOnlyList<ContractPanelResponseDto>>> GetPanelsAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractOverviewDto>> AddPanelAsync(
            int contractId,
            ContractPanelRequestDto request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractOverviewDto>> UpdatePanelAsync(
            int contractId,
            int panelId,
            ContractPanelRequestDto request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractPanelResponseDto>> ConfirmPanelAsync(
            int contractId,
            int panelId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractOverviewDto>> StartBindingAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<ContractOverviewDto>> CancelContractAsync(
            int contractId,
            CancellationToken cancellationToken = default);
    }
}
