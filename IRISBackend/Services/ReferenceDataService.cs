using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Repositories;

namespace IRISBackend.Services
{
    public sealed class ReferenceDataService : IReferenceDataService
    {
        private static readonly ContractTypeReferenceDto[] ContractTypes =
        [
            new()
            {
                Code = "FACULTATIVE",
                DisplayName = "Facultative",
                Description = "Reinsurance for a single risk or defined risk package.",
                IsImplemented = true
            },
            new()
            {
                Code = "QUOTA_SHARE",
                DisplayName = "Quota Share",
                Description = "Fixed percentage sharing of premiums and losses.",
                IsImplemented = false
            },
            new()
            {
                Code = "SURPLUS",
                DisplayName = "Surplus",
                Description = "Reinsurance for amounts exceeding a defined retention.",
                IsImplemented = false
            },
            new()
            {
                Code = "NON_PROPORTIONAL",
                DisplayName = "Non-Proportional",
                Description = "Excess-of-loss protection for a portfolio.",
                IsImplemented = false
            }
        ];

        private readonly IReferenceDataRepository _referenceDataRepository;
        private readonly IMapper _mapper;

        public ReferenceDataService(IReferenceDataRepository referenceDataRepository, IMapper mapper)
        {
            _referenceDataRepository = referenceDataRepository;
            _mapper = mapper;
        }

        public Task<ServiceResult<IEnumerable<ContractTypeReferenceDto>>> GetContractTypesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<IEnumerable<ContractTypeReferenceDto>>.Success(ContractTypes));
        }

        public async Task<ServiceResult<IEnumerable<CurrencyReferenceDto>>> GetActiveCurrenciesAsync(CancellationToken cancellationToken = default)
        {
            var currencies = await _referenceDataRepository.ListActiveCurrenciesAsync(cancellationToken);
            return ServiceResult<IEnumerable<CurrencyReferenceDto>>.Success(_mapper.Map<IEnumerable<CurrencyReferenceDto>>(currencies));
        }

        public async Task<ServiceResult<PagedResponseDto<CedentLookupItemDto>>> SearchCedentsAsync(
            CedentLookupQueryDto query,
            CancellationToken cancellationToken = default)
        {
            var page = Math.Max(query.Page, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 50);
            var (items, totalCount) = await _referenceDataRepository.SearchActiveCedentsAsync(query.Search, page, pageSize, cancellationToken);

            return ServiceResult<PagedResponseDto<CedentLookupItemDto>>.Success(new PagedResponseDto<CedentLookupItemDto>
            {
                Items = _mapper.Map<IEnumerable<CedentLookupItemDto>>(items),
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }
    }
}
