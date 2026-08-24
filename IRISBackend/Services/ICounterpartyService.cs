using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface ICounterpartyService
    {
        Task<ServiceResult<CounterpartyDetailsDto>> CreateAsync(CounterpartyCreateDto request, CancellationToken cancellationToken = default);

        Task<ServiceResult<CounterpartyDetailsDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<ServiceResult<PagedResponseDto<CounterpartyListItemDto>>> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default);

        Task<ServiceResult<CounterpartyDetailsDto>> UpdateAsync(int id, CounterpartyUpdateDto request, CancellationToken cancellationToken = default);

        Task<ServiceResult<CounterpartyDetailsDto>> SetActiveStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default);
    }
}
