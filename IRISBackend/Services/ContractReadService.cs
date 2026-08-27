using IRISBackend.DTOs;
using IRISBackend.Repositories;

namespace IRISBackend.Services
{
    public sealed class ContractReadService : IContractReadService
    {
        private readonly IContractRepository _contractRepository;
        private readonly IUserService _userService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ContractReadService(
            IContractRepository contractRepository,
            IUserService userService,
            IHttpContextAccessor httpContextAccessor)
        {
            _contractRepository = contractRepository;
            _userService = userService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ServiceResult<PagedResponseDto<ContractListItemDto>>> ListAsync(
            ContractListQueryDto query,
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure<PagedResponseDto<ContractListItemDto>>(userResult);
            }

            var pageNumber = Math.Max(query.PageNumber, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var normalizedQuery = NormalizeQuery(query, pageNumber, pageSize);
            var (items, totalCount) = await _contractRepository.ListAsync(
                normalizedQuery,
                userResult.Value!.Id,
                cancellationToken);

            return ServiceResult<PagedResponseDto<ContractListItemDto>>.Success(new PagedResponseDto<ContractListItemDto>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }

        public async Task<ServiceResult<ContractDashboardSummaryDto>> GetDashboardSummaryAsync(
            CancellationToken cancellationToken = default)
        {
            var userResult = await GetCurrentFrontOfficerAsync();
            if (!userResult.Succeeded)
            {
                return MapFailure<ContractDashboardSummaryDto>(userResult);
            }

            var data = await _contractRepository.GetDashboardDataAsync(userResult.Value!.Id, cancellationToken);
            var distribution = data.StatusCounts
                .Select(status => new ContractWorkflowDistributionDto
                {
                    Status = status.Status,
                    Count = status.Count,
                    Percentage = data.TotalContracts == 0
                        ? 0m
                        : Math.Round(status.Count * 100m / data.TotalContracts, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();

            return ServiceResult<ContractDashboardSummaryDto>.Success(new ContractDashboardSummaryDto
            {
                TotalContracts = data.TotalContracts,
                PendingReviewContracts = SumCategory(data.StatusCounts, ContractWorkflowStatusCategories.PendingReview),
                BoundActiveContracts = SumCategory(data.StatusCounts, ContractWorkflowStatusCategories.BoundActive),
                ActionRequiredContracts = data.ActionRequiredContracts,
                WorkflowDistribution = distribution,
                RecentContracts = data.RecentContracts
            });
        }

        private async Task<ServiceResult<UserProfileResponseDto>> GetCurrentFrontOfficerAsync()
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User identity was not found in the token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var userResult = await _userService.GetCurrentUserAsync(principal);
            if (!userResult.Succeeded || userResult.Value is null)
            {
                return userResult;
            }

            return FrontOfficerAccessRules.IsFrontOfficer(userResult.Value)
                ? userResult
                : ServiceResult<UserProfileResponseDto>.Failure(
                    "Only Front Officers can access contract workspace data.",
                    statusCode: StatusCodes.Status403Forbidden);
        }

        private static ContractListQueryDto NormalizeQuery(ContractListQueryDto query, int pageNumber, int pageSize)
        {
            return new ContractListQueryDto
            {
                Search = query.Search?.Trim(),
                ContractType = ContractTypeNames.NormalizeFilter(query.ContractType),
                Status = query.Status?.Trim(),
                CounterpartyId = query.CounterpartyId,
                CurrencyId = query.CurrencyId,
                InceptionFrom = query.InceptionFrom,
                InceptionTo = query.InceptionTo,
                ExpiryFrom = query.ExpiryFrom,
                ExpiryTo = query.ExpiryTo,
                PageNumber = pageNumber,
                PageSize = pageSize,
                SortBy = query.SortBy.Trim(),
                SortDirection = query.SortDirection.Trim()
            };
        }

        private static int SumCategory(IEnumerable<ContractStatusCount> counts, IReadOnlySet<string> statuses)
        {
            return counts.Where(item => statuses.Contains(item.Status)).Sum(item => item.Count);
        }

        private static ServiceResult<T> MapFailure<T>(ServiceResult<UserProfileResponseDto> result)
        {
            return ServiceResult<T>.Failure(
                result.Error?.Message ?? "Unable to resolve the current user.",
                result.Error?.Errors,
                result.StatusCode);
        }
    }
}
