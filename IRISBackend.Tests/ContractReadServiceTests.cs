using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Claims;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests
{
    public sealed class ContractReadServiceTests
    {
        [Fact]
        public async Task GetDashboardSummaryAsync_CalculatesTotalsFlagsAndPercentages()
        {
            var repository = new FakeContractRepository
            {
                DashboardData = new ContractDashboardData
                {
                    TotalContracts = 4,
                    ActionRequiredContracts = 2,
                    StatusCounts =
                    [
                        new ContractStatusCount { Status = ContractConstants.DraftStatus, Count = 1 },
                        new ContractStatusCount { Status = "Submitted", Count = 3 }
                    ]
                }
            };
            var service = CreateService(repository);

            var result = await service.GetDashboardSummaryAsync();

            Assert.True(result.Succeeded);
            Assert.Equal(4, result.Value!.TotalContracts);
            Assert.Equal(1, result.Value.PendingReviewContracts);
            Assert.Equal(2, result.Value.ActionRequiredContracts);
            Assert.Equal(25m, result.Value.WorkflowDistribution.Single(item => item.Status == ContractConstants.DraftStatus).Percentage);
            Assert.Equal(75m, result.Value.WorkflowDistribution.Single(item => item.Status == "Submitted").Percentage);
        }

        [Fact]
        public async Task GetDashboardSummaryAsync_WhenNoContracts_ReturnsZeroPercentages()
        {
            var service = CreateService(new FakeContractRepository
            {
                DashboardData = new ContractDashboardData
                {
                    TotalContracts = 0,
                    StatusCounts = []
                }
            });

            var result = await service.GetDashboardSummaryAsync();

            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Value!.TotalContracts);
            Assert.Empty(result.Value.WorkflowDistribution);
        }

        [Fact]
        public async Task ListAsync_WhenRequestIsUnauthenticated_ReturnsUnauthorized()
        {
            var service = CreateService(new FakeContractRepository(), authenticated: false);

            var result = await service.ListAsync(new ContractListQueryDto());

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        }

        private static ContractReadService CreateService(FakeContractRepository repository, bool authenticated = true)
        {
            var user = new UserProfileResponseDto
            {
                Id = "user-1",
                Department = ContractConstants.FrontOfficerDepartment,
                Position = ContractConstants.FrontOfficerPosition,
                Roles = [IdentityRoles.Employee]
            };
            var identity = authenticated
                ? new ClaimsIdentity([new SecurityClaim(ClaimTypes.NameIdentifier, user.Id)], "TestAuth")
                : new ClaimsIdentity();
            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            return new ContractReadService(repository, new FakeUserService(user), accessor);
        }

        private sealed class FakeUserService : IUserService
        {
            private readonly UserProfileResponseDto _user;

            public FakeUserService(UserProfileResponseDto user)
            {
                _user = user;
            }

            public Task<ServiceResult<UserProfileResponseDto>> GetCurrentUserAsync(ClaimsPrincipal principal)
            {
                return Task.FromResult(ServiceResult<UserProfileResponseDto>.Success(_user));
            }
        }

        private sealed class FakeContractRepository : IContractRepository
        {
            public ContractDashboardData DashboardData { get; init; } = new();

            public Task<(IReadOnlyList<ContractListItemDto> Items, int TotalCount)> ListAsync(
                ContractListQueryDto query,
                string userId,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(((IReadOnlyList<ContractListItemDto>)Array.Empty<ContractListItemDto>(), 0));
            }

            public Task<ContractDashboardData> GetDashboardDataAsync(string userId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(DashboardData);
            }

            public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task AddAsync(Contract contract, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public void Update(Contract contract) => throw new NotSupportedException();
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<Contract?> GetFacultativeContractByIdAsync(int contractId, bool asNoTracking = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
