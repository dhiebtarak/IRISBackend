using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests
{
    public sealed class ContractPlacementServiceTests
    {
        [Fact]
        public async Task GetPanelsAsync_WhenContractHasNoPanels_ReturnsEmptyArray()
        {
            var repository = new FakeContractPlacementRepository
            {
                Contract = CreateContract()
            };
            var service = CreateService(repository);

            var result = await service.GetPanelsAsync(10);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Value!);
        }

        [Fact]
        public async Task GetPanelsAsync_WhenContractDoesNotExist_ReturnsNotFound()
        {
            var repository = new FakeContractPlacementRepository
            {
                Contract = CreateContract()
            };
            var service = CreateService(repository);

            var result = await service.GetPanelsAsync(999);

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        }

        [Fact]
        public async Task GetPanelsAsync_WhenPanelsExist_ReturnsReinsurerPanelDetails()
        {
            var contract = CreateContract();
            contract.ContractPanels.Add(new ContractPanel
            {
                IdPanel = 1,
                IdContract = 10,
                IdCounterparty = 7,
                IdCounterpartyNavigation = new Counterparty
                {
                    Id = 7,
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
            });
            var repository = new FakeContractPlacementRepository
            {
                Contract = contract
            };
            var service = CreateService(repository);

            var result = await service.GetPanelsAsync(10);

            Assert.True(result.Succeeded);
            var panel = Assert.Single(result.Value!);
            Assert.Equal(1, panel.IdPanel);
            Assert.Equal(10, panel.IdContract);
            Assert.Equal(7, panel.IdCounterparty);
            Assert.Equal("Tunis Re", panel.ReinsurerName);
            Assert.Equal("Reinsurer", panel.Role);
            Assert.Equal(25m, panel.WrittenSharePct);
            Assert.Equal(20m, panel.SignedSharePct);
            Assert.Equal(1000000m, panel.Tsi);
            Assert.Equal(5000m, panel.Deductible);
            Assert.Equal(1.5m, panel.PremiumRatePct);
            Assert.Equal(15000m, panel.GrossPremium);
            Assert.Equal(10m, panel.BrokeragePct);
            Assert.Equal(13000m, panel.NetPremium);
            Assert.Equal(500m, panel.OtherDeductions);
            Assert.Equal("REF-REINS-001", panel.PremiumReference);
            Assert.Equal(ContractConstants.DraftStatus, panel.Status);
        }

        [Fact]
        public async Task AddPanelAsync_WhenFirstPanelIsAdded_UpdatesContractStatusAndCalculatesNetPremium()
        {
            var repository = new FakeContractPlacementRepository
            {
                Contract = CreateContract()
            };
            var service = CreateService(repository);

            var result = await service.AddPanelAsync(10, CreateRequest());

            Assert.True(result.Succeeded);
            Assert.Single(repository.Contract.ContractPanels);
            Assert.Equal(ContractConstants.PlacementInProgressStatus, repository.Contract.Status);
            Assert.Equal(13000m, repository.Contract.ContractPanels.Single().NetPremium);
            Assert.Equal(ContractConstants.PlacementInProgressStatus, result.Value!.Status);
        }

        [Fact]
        public async Task AddPanelAsync_WhenReinsurerAlreadyExists_ReturnsBadRequest()
        {
            var contract = CreateContract();
            contract.ContractPanels.Add(new ContractPanel { IdPanel = 1, IdContract = 10, IdCounterparty = 7 });
            var repository = new FakeContractPlacementRepository
            {
                Contract = contract
            };
            var service = CreateService(repository);

            var result = await service.AddPanelAsync(10, CreateRequest());

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Contains("already assigned", result.Error!.Errors.Single());
        }

        [Fact]
        public async Task StartBindingAsync_WhenNoPanelExists_ReturnsBadRequest()
        {
            var repository = new FakeContractPlacementRepository
            {
                Contract = CreateContract()
            };
            var service = CreateService(repository);

            var result = await service.StartBindingAsync(10);

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        }

        [Fact]
        public async Task StartBindingAsync_WhenNoConfirmedPanelExists_ReturnsBadRequest()
        {
            var contract = CreateContract();
            contract.ContractPanels.Add(new ContractPanel { IdPanel = 1, IdContract = 10, IdCounterparty = 7, Status = ContractConstants.DraftStatus });
            var repository = new FakeContractPlacementRepository
            {
                Contract = contract
            };
            var service = CreateService(repository);

            var result = await service.StartBindingAsync(10);

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Equal("Please confirm at least one reinsurer before starting the binding phase.", result.Error!.Message);
        }

        [Fact]
        public async Task StartBindingAsync_WhenConfirmedPanelExists_UpdatesContractAndKeepsPanelConfirmed()
        {
            var contract = CreateContract();
            contract.ContractPanels.Add(new ContractPanel { IdPanel = 1, IdContract = 10, IdCounterparty = 7, Status = ContractConstants.ConfirmedPanelStatus });
            var repository = new FakeContractPlacementRepository
            {
                Contract = contract
            };
            var service = CreateService(repository);

            var result = await service.StartBindingAsync(10);

            Assert.True(result.Succeeded);
            Assert.Equal(ContractConstants.BindingInProgressStatus, repository.Contract.Status);
            Assert.Equal(ContractConstants.ConfirmedPanelStatus, repository.Contract.ContractPanels.Single().Status);
        }

        [Fact]
        public async Task ConfirmPanelAsync_WhenRequiredValuesAreMissing_ReturnsBadRequest()
        {
            var contract = CreateContract();
            contract.ContractPanels.Add(new ContractPanel
            {
                IdPanel = 1,
                IdContract = 10,
                IdCounterparty = 7,
                Status = ContractConstants.DraftStatus
            });
            var repository = new FakeContractPlacementRepository
            {
                Contract = contract
            };
            var service = CreateService(repository);

            var result = await service.ConfirmPanelAsync(10, 1);

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Contains(result.Error!.Errors, error => error.Contains("writtenSharePct is required", StringComparison.Ordinal));
            Assert.Contains(result.Error.Errors, error => error.Contains("signedSharePct is required", StringComparison.Ordinal));
            Assert.Contains(result.Error.Errors, error => error.Contains("grossPremium is required", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ConfirmPanelAsync_WhenRequiredValuesExist_UpdatesPanelStatus()
        {
            var contract = CreateContract();
            contract.ContractPanels.Add(new ContractPanel
            {
                IdPanel = 1,
                IdContract = 10,
                IdCounterparty = 7,
                IdCounterpartyNavigation = new Counterparty { Id = 7, Name = "Tunis Re" },
                Status = ContractConstants.DraftStatus,
                WrittenSharePct = 25m,
                SignedSharePct = 20m,
                GrossPremium = 15000m
            });
            var repository = new FakeContractPlacementRepository
            {
                Contract = contract
            };
            var service = CreateService(repository);

            var result = await service.ConfirmPanelAsync(10, 1);

            Assert.True(result.Succeeded);
            Assert.Equal(ContractConstants.ConfirmedPanelStatus, repository.Contract.ContractPanels.Single().Status);
            Assert.Equal(ContractConstants.ConfirmedPanelStatus, result.Value!.Status);
        }

        private static ContractPlacementService CreateService(FakeContractPlacementRepository repository)
        {
            var user = new UserProfileResponseDto
            {
                Id = "user-1",
                Department = ContractConstants.FrontOfficerDepartment,
                Position = ContractConstants.FrontOfficerPosition,
                Roles = [IdentityRoles.Employee]
            };

            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new SecurityClaim(ClaimTypes.NameIdentifier, user.Id)
                    ], "TestAuth"))
                }
            };

            return new ContractPlacementService(
                repository,
                new FakeContractReadService(repository),
                new FakeUserService(user),
                accessor);
        }

        private static Contract CreateContract()
        {
            return new Contract
            {
                IdContract = 10,
                Name = "Property Facultative",
                TypeOfBusiness = ContractConstants.FacultativeType,
                Status = ContractConstants.ReadyForPlacementStatus,
                IdUser = "user-1",
                IsCurrent = true,
                ContractPanels = []
            };
        }

        private static ContractPanelRequestDto CreateRequest()
        {
            return new ContractPanelRequestDto
            {
                IdCounterparty = 7,
                WrittenSharePct = 25m,
                SignedSharePct = 20m,
                Tsi = 1000000m,
                Deductible = 5000m,
                PremiumRatePct = 1.5m,
                GrossPremium = 15000m,
                BrokeragePct = 10m,
                OtherDeductions = 500m,
                PremiumReference = "REF-REINS-001",
                Status = ContractConstants.DraftStatus
            };
        }

        private sealed class FakeContractPlacementRepository : IContractPlacementRepository
        {
            public Contract Contract { get; init; } = new();

            public bool ReinsurerIsActive { get; init; } = true;

            public Task<Contract?> GetContractForPlacementAsync(int contractId, string userId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Contract.IdContract == contractId && Contract.IdUser == userId ? Contract : null);
            }

            public Task<ContractPanel?> GetPanelAsync(int contractId, int panelId, string userId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Contract.ContractPanels.SingleOrDefault(panel => panel.IdContract == contractId && panel.IdPanel == panelId));
            }

            public Task<IReadOnlyList<ContractPanelResponseDto>> GetPanelsAsync(int contractId, string userId, CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ContractPanelResponseDto> panels = Contract.IdContract == contractId && Contract.IdUser == userId
                    ? Contract.ContractPanels
                        .OrderBy(panel => panel.IdPanel)
                        .Select(panel => new ContractPanelResponseDto
                        {
                            IdPanel = panel.IdPanel,
                            IdContract = panel.IdContract,
                            IdCounterparty = panel.IdCounterparty,
                            ReinsurerName = panel.IdCounterpartyNavigation?.Name ?? string.Empty,
                            Role = panel.Role ?? string.Empty,
                            WrittenSharePct = panel.WrittenSharePct,
                            SignedSharePct = panel.SignedSharePct,
                            Tsi = panel.Tsi,
                            Deductible = panel.Deductible,
                            PremiumRatePct = panel.PremiumRatePct,
                            GrossPremium = panel.GrossPremium,
                            BrokeragePct = panel.BrokeragePct,
                            OtherDeductions = panel.OtherDeductions,
                            NetPremium = panel.NetPremium,
                            PremiumReference = panel.PremiumReference,
                            Status = panel.Status ?? string.Empty
                        })
                        .ToArray()
                    : [];

                return Task.FromResult(panels);
            }

            public Task<bool> IsActiveReinsurerAsync(int counterpartyId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ReinsurerIsActive && counterpartyId == 7);
            }

            public Task<bool> ContractHasReinsurerPanelAsync(
                int contractId,
                int counterpartyId,
                int? excludedPanelId = null,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Contract.ContractPanels.Any(panel =>
                    panel.IdContract == contractId &&
                    panel.IdCounterparty == counterpartyId &&
                    (!excludedPanelId.HasValue || panel.IdPanel != excludedPanelId.Value)));
            }

            public Task AddPanelAsync(ContractPanel panel, CancellationToken cancellationToken = default)
            {
                panel.IdPanel = Contract.ContractPanels.Count + 1;
                panel.IdContractNavigation = Contract;
                Contract.ContractPanels.Add(panel);
                return Task.CompletedTask;
            }

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(1);
            }
        }

        private sealed class FakeContractReadService : IContractReadService
        {
            private readonly FakeContractPlacementRepository _repository;

            public FakeContractReadService(FakeContractPlacementRepository repository)
            {
                _repository = repository;
            }

            public Task<ServiceResult<PagedResponseDto<ContractListItemDto>>> ListAsync(ContractListQueryDto query, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<ServiceResult<ContractDashboardSummaryDto>> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<ServiceResult<ContractOverviewDto>> GetOverviewAsync(int contractId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ServiceResult<ContractOverviewDto>.Success(new ContractOverviewDto
                {
                    IdContract = contractId,
                    Status = _repository.Contract.Status,
                    Panels = _repository.Contract.ContractPanels.Select(panel => new ContractPanelResponseDto
                    {
                        IdPanel = panel.IdPanel,
                        IdContract = panel.IdContract,
                        IdCounterparty = panel.IdCounterparty,
                        NetPremium = panel.NetPremium,
                        Status = panel.Status
                    }).ToArray()
                }));
            }

            public Task<ServiceResult<SlipDownloadResult>> DownloadSlipsAsync(int contractId, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
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
    }
}
