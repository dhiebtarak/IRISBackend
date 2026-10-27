using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface IFacultativeContractService
    {
        Task<ServiceResult<FacultativeContractResponseDto>> CreateDraftAsync(
            UpsertFacultativeContractRequest request,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<FacultativeContractResponseDto>> GetByIdAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<FacultativeContractResponseDto>> CompleteDraftAsync(
            int contractId,
            CancellationToken cancellationToken = default);

        Task<ServiceResult<FacultativeContractResponseDto>> UpdateDraftAsync(
            int contractId,
            UpsertFacultativeContractRequest request,
            CancellationToken cancellationToken = default);
    }
}
