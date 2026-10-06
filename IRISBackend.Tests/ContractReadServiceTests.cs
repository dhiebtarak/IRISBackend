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

        [Fact]
        public async Task GetOverviewAsync_WhenContractExists_ReturnsCoverageAndSlipAvailability()
        {
            var repository = new FakeContractRepository
            {
                OverviewContract = CreateOverviewContract()
            };
            var service = CreateService(repository);

            var result = await service.GetOverviewAsync(10);

            Assert.True(result.Succeeded);
            Assert.Equal(10, result.Value!.IdContract);
            Assert.Equal("PD", result.Value.CoverageSelection!.MainCover!.Code);
            Assert.Equal([2], result.Value.CoverageSelection.SectionIds);
            Assert.Equal(10m, result.Value.WrittenSharePct);
            Assert.Equal(9m, result.Value.SignedSharePct);
            Assert.Equal(15000000m, result.Value.Tsi);
            Assert.Equal(500000m, result.Value.Deductible);
            Assert.Equal(0.045m, result.Value.PremiumRate);
            Assert.Equal(100000m, result.Value.GrossPremium);
            Assert.Equal(20m, result.Value.CedentCommissionPct);
            Assert.Equal(80000m, result.Value.NetPremiumCedent);
            Assert.True(result.Value.IsFronting);
            Assert.True(result.Value.IsHighPriority);
            Assert.Equal("user-1", result.Value.CreatedBy!.Id);
            var panel = Assert.Single(result.Value.Panels);
            Assert.Equal(1000000m, panel.Tsi);
            Assert.Equal(1.5m, panel.PremiumRatePct);
            Assert.Equal(10m, panel.BrokeragePct);
            Assert.Equal("REF-REINS-001", panel.PremiumReference);
            Assert.Equal(2, result.Value.SlipAvailability.SelectedCoverageItemCount);
            Assert.Equal(1, result.Value.SlipAvailability.AvailableSlipCount);
            Assert.Equal(1, result.Value.SlipAvailability.MissingSlipCount);
        }

        [Fact]
        public async Task DownloadSlipsAsync_WhenNoSlipExists_ReturnsNonBlockingUnavailableResponse()
        {
            var repository = new FakeContractRepository
            {
                OverviewContract = CreateOverviewContract()
            };
            var service = CreateService(repository);

            var result = await service.DownloadSlipsAsync(10);

            Assert.True(result.Succeeded);
            Assert.False(result.Value!.Available);
            Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
            Assert.Equal("Slip is not available for this contract selection.", result.Value.Message);
        }

        [Fact]
        public async Task DownloadSlipsAsync_WhenOneSlipExists_ReturnsSlipFile()
        {
            var repository = new FakeContractRepository
            {
                OverviewContract = CreateOverviewContract(),
                SlipTemplates =
                [
                    new SlipTemplate
                    {
                        IdSlipTemplate = 5,
                        IdCoverageItem = 1,
                        OriginalFileName = "property-slip.docx",
                        FileExtension = ".docx",
                        MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        FileContent = [1, 2, 3],
                        IsActive = true,
                        IsCurrent = true
                    }
                ]
            };
            var service = CreateService(repository);

            var result = await service.DownloadSlipsAsync(10);

            Assert.True(result.Succeeded);
            Assert.Equal("property-slip.docx", result.Value!.FileName);
            Assert.Equal([1, 2, 3], result.Value.Content);
        }

        [Fact]
        public async Task GetOverviewAsync_WhenDraftExists_IsReadOnly()
        {
            var repository = new FakeContractRepository
            {
                OverviewContract = CreateOverviewContract()
            };
            repository.OverviewContract.Status = ContractConstants.DraftStatus;
            var service = CreateService(repository);

            var result = await service.GetOverviewAsync(10);

            Assert.True(result.Succeeded);
            Assert.Null(repository.ReadyForPlacementContractId);
            Assert.Equal(ContractConstants.DraftStatus, repository.OverviewContract.Status);
        }

        [Fact]
        public async Task CompleteDraftAsync_MovesDraftToReadyForPlacementThenReadsOverview()
        {
            var repository = new FakeContractRepository
            {
                OverviewContract = CreateOverviewContract()
            };
            var service = CreateService(repository);

            var result = await service.CompleteDraftAsync(10);

            Assert.True(result.Succeeded);
            Assert.Equal(10, repository.ReadyForPlacementContractId);
            Assert.Equal(ContractConstants.ReadyForPlacementStatus, repository.OverviewContract.Status);
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

        private static Contract CreateOverviewContract()
        {
            return new Contract
            {
                IdContract = 10,
                Umr = "FAC-10",
                Name = "Property Facultative",
                TypeOfBusiness = ContractConstants.FacultativeType,
                Status = ContractConstants.DraftStatus,
                IdCounterparty = 3,
                IdCurrency = 1,
                CreationDate = new DateOnly(2026, 8, 31),
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                WrittenSharePctCedent = 10m,
                SignedSharePctCedent = 9m,
                GrossPremium = 100000m,
                CedentCommission = 20m,
                NetPremiumCedent = 80000m,
                Fees = 1000m,
                IsFronting = true,
                FrontingReference = "FRONT-1",
                Flag = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "user-1",
                CreatedByNavigation = new AspNetUser
                {
                    Id = "user-1",
                    FullName = "Front Officer",
                    UserName = "front.officer",
                    Email = "front.officer@iris.local"
                },
                IdUser = "user-1",
                IsCurrent = true,
                ContractFac = new ContractFac
                {
                    IdContract = 10,
                    SumInsured = 15000000m,
                    Deductible = 500000m,
                    PremiumRate = 0.045m
                },
                IdCounterpartyNavigation = new Counterparty
                {
                    Id = 3,
                    Name = "ami",
                    Country = "Tunisie",
                    City = "Tunis"
                },
                IdCurrencyNavigation = new Currency
                {
                    IdCurrency = 1,
                    CurrencyCode = "TND",
                    CurrencySymbol = "DT",
                    Name = "Tunisian Dinar",
                    IsDefault = true
                },
                ContractCoverageSelections =
                [
                    new ContractCoverageSelection
                    {
                        IdContract = 10,
                        IdCoverageItem = 1,
                        IdSlipTemplate = 5,
                        IdCoverageItemNavigation = new CoverageItem
                        {
                            IdCoverageItem = 1,
                            CoverageLevel = CoverageConstants.MainCoverLevel,
                            CoverageCode = "PD",
                            CoverageName = "Property Damage",
                            IsActive = true
                        }
                    },
                    new ContractCoverageSelection
                    {
                        IdContract = 10,
                        IdCoverageItem = 2,
                        IdCoverageItemNavigation = new CoverageItem
                        {
                            IdCoverageItem = 2,
                            CoverageLevel = CoverageConstants.SectionLevel,
                            CoverageCode = "FIRE",
                            CoverageName = "Fire",
                            IsActive = true
                        }
                    }
                ],
                ContractPanels =
                [
                    new ContractPanel
                    {
                        IdPanel = 1,
                        IdContract = 10,
                        IdCounterparty = 4,
                        IdCounterpartyNavigation = new Counterparty
                        {
                            Id = 4,
                            Name = "Tunis Re",
                            Type = "Reinsurer"
                        },
                        Role = "Reinsurer",
                        WrittenSharePct = 25m,
                        SignedSharePct = 20m,
                        Tsi = 1000000m,
                        Deductible = 5000m,
                        PremiumRatePct = 1.5m,
                        GrossPremium = 15000m,
                        BrokeragePct = 10m,
                        NetPremium = 13000m,
                        OtherDeductions = 500m,
                        PremiumReference = "REF-REINS-001",
                        Status = ContractConstants.DraftStatus
                    }
                ]
            };
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

            public Contract? OverviewContract { get; init; }

            public IReadOnlyList<SlipTemplate> SlipTemplates { get; init; } = [];

            public int? ReadyForPlacementContractId { get; private set; }

            public Task<(IReadOnlyList<ContractListItemDto> Items, int TotalCount)> ListAsync(
                ContractListQueryDto query,
                string userId,
                bool includeAllContracts = false,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(((IReadOnlyList<ContractListItemDto>)Array.Empty<ContractListItemDto>(), 0));
            }

            public Task<ContractDashboardData> GetDashboardDataAsync(string userId, bool includeAllContracts = false, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(DashboardData);
            }

            public Task<Contract?> GetAccessibleContractByIdAsync(
                int contractId,
                string userId,
                bool includeAllContracts = false,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(
                    OverviewContract is not null &&
                    OverviewContract.IdContract == contractId &&
                    (includeAllContracts || OverviewContract.IdUser == userId)
                        ? OverviewContract
                        : null);
            }

            public Task MarkDraftReadyForPlacementAsync(
                int contractId,
                string userId,
                CancellationToken cancellationToken = default)
            {
                if (OverviewContract is not null &&
                    OverviewContract.IdContract == contractId &&
                    OverviewContract.IdUser == userId &&
                    OverviewContract.Status == ContractConstants.DraftStatus)
                {
                    ReadyForPlacementContractId = contractId;
                    OverviewContract.Status = ContractConstants.ReadyForPlacementStatus;
                    OverviewContract.UpdatedBy = userId;
                    OverviewContract.UpdatedAt = DateTime.UtcNow;
                }

                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<SlipTemplate>> ListActiveCurrentSlipTemplatesForContractAsync(
                int contractId,
                string userId,
                bool includeAllContracts = false,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(SlipTemplates);
            }

            public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<int> AllocateUmrSequenceAsync(int inceptionYear, string contractTypeCode, string mainCoverCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task AddAsync(Contract contract, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public void Update(Contract contract) => throw new NotSupportedException();
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<Contract?> GetFacultativeContractByIdAsync(int contractId, bool asNoTracking = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
