using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface IReferenceDataService
    {
        Task<ServiceResult<IEnumerable<ContractTypeReferenceDto>>> GetContractTypesAsync(CancellationToken cancellationToken = default);

        Task<ServiceResult<IEnumerable<CurrencyReferenceDto>>> GetActiveCurrenciesAsync(CancellationToken cancellationToken = default);

        Task<ServiceResult<PagedResponseDto<CedentLookupItemDto>>> SearchCedentsAsync(
            CedentLookupQueryDto query,
            CancellationToken cancellationToken = default);
    }
}
