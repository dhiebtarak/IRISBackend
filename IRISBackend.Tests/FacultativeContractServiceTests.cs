using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Mappings;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Security.Claims;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests
{
    public class FacultativeContractServiceTests
    {
        [Fact]
        public async Task CreateDraftAsync_WhenRequestIsValid_CreatesContractAndFacultativeDetailsAndSetsServerFields()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            var service = CreateService(contractRepository, referenceDataRepository);

            var result = await service.CreateDraftAsync(CreateValidRequest());

            Assert.True(result.Succeeded);
            Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
            Assert.Single(contractRepository.Contracts);
            Assert.Single(contractRepository.ContractFacs);

            var contract = contractRepository.Contracts[0];
            var contractFac = contractRepository.ContractFacs[0];

            Assert.Equal(ContractConstants.FacultativeType, contract.TypeOfBusiness);
            Assert.Equal(ContractConstants.DraftStatus, contract.Status);
            Assert.Equal("user-1", contract.IdUser);
            Assert.Equal("user-1", contract.CreatedBy);
            Assert.True(contract.IsCurrent);
            Assert.NotEqual(default, contract.CreatedAt);
            Assert.Equal(DateOnly.FromDateTime(contract.CreatedAt), contract.CreationDate);
            Assert.Equal(contract.IdContract, contractFac.IdContract);
            Assert.Equal(450000m, result.Value!.NetPremiumCedent);
            Assert.Equal("AXA Cedant", result.Value.Cedent.Name);
            Assert.Equal("USD", result.Value.Currency.Code);
        }

        [Fact]
        public async Task CreateDraftAsync_WhenRequestHasCoverage_CreatesCoverageSelectionsInSameWorkflow()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            var coverageSelectionService = new FakeCoverageSelectionService();
            var service = CreateService(contractRepository, referenceDataRepository, coverageSelectionService: coverageSelectionService);

            var result = await service.CreateDraftAsync(CreateValidRequest());

            Assert.True(result.Succeeded);
            Assert.Equal(1, coverageSelectionService.CreatedContractIds.Single());
            Assert.Equal("user-1", coverageSelectionService.CreatedByUserIds.Single());
        }

        [Fact]
        public async Task CreateDraftAsync_WhenContractFacSaveFails_RollsBackContract()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository)
            {
                FailOnNextContractFacSave = true
            };
            var service = CreateService(contractRepository, referenceDataRepository);

            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateDraftAsync(CreateValidRequest()));

            Assert.Empty(contractRepository.Contracts);
            Assert.Empty(contractRepository.ContractFacs);
        }

        [Fact]
        public async Task CreateDraftAsync_WhenCoverageSaveFails_RollsBackContractAndFacultativeDetails()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            var coverageSelectionService = new FakeCoverageSelectionService
            {
                FailCreate = true
            };
            var service = CreateService(contractRepository, referenceDataRepository, coverageSelectionService: coverageSelectionService);

            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateDraftAsync(CreateValidRequest()));

            Assert.Empty(contractRepository.Contracts);
            Assert.Empty(contractRepository.ContractFacs);
        }

        [Fact]
        public async Task CreateDraftAsync_WhenCurrencyIsInactive_ReturnsBadRequest()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            referenceDataRepository.Currencies[0].IsActive = false;
            var service = CreateService(new FakeContractRepository(referenceDataRepository), referenceDataRepository);

            var result = await service.CreateDraftAsync(CreateValidRequest());

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Contains("currency", result.Error!.Errors.Single(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateDraftAsync_WhenCedentIsInactive_ReturnsBadRequest()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            referenceDataRepository.Counterparties[0].IsActive = false;
            var service = CreateService(new FakeContractRepository(referenceDataRepository), referenceDataRepository);

            var result = await service.CreateDraftAsync(CreateValidRequest());

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Contains("cedent", result.Error!.Errors.Single(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateDraftAsync_WhenUmrAlreadyExists_ReturnsConflict()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            contractRepository.Contracts.Add(new Contract
            {
                IdContract = 8,
                Umr = "B1234FAC2024-X",
                Name = "Existing Draft",
                IdCounterparty = 10,
                IdCurrency = 2,
                IdUser = "user-9",
                TypeOfBusiness = ContractConstants.FacultativeType,
                Status = ContractConstants.DraftStatus,
                CreationDate = new DateOnly(2026, 8, 19),
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "user-9",
                IsCurrent = true
            });

            var service = CreateService(contractRepository, referenceDataRepository);
            var result = await service.CreateDraftAsync(CreateValidRequest());

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Equal("A contract with the same UMR already exists.", result.Error!.Message);
        }

        [Fact]
        public async Task CreateDraftAsync_WhenUserIsNotFrontOfficer_ReturnsForbidden()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var currentUser = CreateFrontOfficerProfile();
            currentUser.Position = "Back officer";
            var service = CreateService(new FakeContractRepository(referenceDataRepository), referenceDataRepository, currentUser);

            var result = await service.CreateDraftAsync(CreateValidRequest());

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [Fact]
        public async Task UpdateDraftAsync_WhenDraftBelongsToAnotherUser_ReturnsForbidden()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            SeedDraft(contractRepository, ownerUserId: "another-user");
            var service = CreateService(contractRepository, referenceDataRepository);

            var result = await service.UpdateDraftAsync(1, CreateValidRequest(name: "Updated Draft"));

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [Fact]
        public async Task UpdateDraftAsync_WhenContractIsNotDraft_ReturnsConflict()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            SeedDraft(contractRepository, status: "SUBMITTED");
            var service = CreateService(contractRepository, referenceDataRepository);

            var result = await service.UpdateDraftAsync(1, CreateValidRequest(name: "Updated Draft"));

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        }

        [Theory]
        [InlineData("Ready for Placement")]
        [InlineData("Placement In Progress")]
        public async Task UpdateDraftAsync_WhenContractIsInEditableWorkflowState_ReturnsSuccess(string status)
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            SeedDraft(contractRepository, status: status);
            var service = CreateService(contractRepository, referenceDataRepository);

            var result = await service.UpdateDraftAsync(1, CreateValidRequest());

            Assert.True(result.Succeeded);
            Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
            Assert.Equal(status, contractRepository.Contracts[0].Status);
        }

        [Fact]
        public async Task GetByIdAsync_WhenFacultativeDraftExists_ReturnsCombinedResponse()
        {
            var referenceDataRepository = new FakeReferenceDataRepository();
            var contractRepository = new FakeContractRepository(referenceDataRepository);
            SeedDraft(contractRepository);
            var service = CreateService(contractRepository, referenceDataRepository);

            var result = await service.GetByIdAsync(1);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Value!.Id);
            Assert.Equal("AXA Cedant", result.Value.Cedent.Name);
            Assert.Equal("London", result.Value.Cedent.City);
            Assert.Equal("USD", result.Value.Currency.Code);
            Assert.Equal(15000000m, result.Value.SumInsured);
            Assert.Equal(0.045m, result.Value.PremiumRate);
        }

        private static FacultativeContractService CreateService(
            FakeContractRepository contractRepository,
            FakeReferenceDataRepository referenceDataRepository,
            UserProfileResponseDto? currentUser = null,
            FakeCoverageSelectionService? coverageSelectionService = null)
        {
            currentUser ??= CreateFrontOfficerProfile();
            coverageSelectionService ??= new FakeCoverageSelectionService();

            var mapper = new MapperConfiguration(configuration =>
            {
                configuration.AddProfile<ContractProfile>();
                configuration.AddProfile<CounterpartyProfile>();
            }, NullLoggerFactory.Instance).CreateMapper();

            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new SecurityClaim(ClaimTypes.NameIdentifier, currentUser.Id)
                    ], "TestAuth"))
                }
            };

            return new FacultativeContractService(
                contractRepository,
                referenceDataRepository,
                coverageSelectionService,
                new FakeUserService(currentUser),
                mapper,
                httpContextAccessor);
        }

        private static UpsertFacultativeContractRequest CreateValidRequest(string name = "Property Facultative 2026")
        {
            return new UpsertFacultativeContractRequest
            {
                Umr = "B1234FAC2024-X",
                Name = name,
                CounterpartyId = 10,
                CurrencyId = 2,
                ReceptionDate = new DateOnly(2026, 8, 19),
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                WrittenSharePct = 10m,
                SignedSharePct = 10m,
                Fees = 0m,
                IsHighPriority = false,
                SumInsured = 12500000m,
                Deductible = 250000m,
                PremiumRate = 0.045m,
                GrossPremium = 562500m,
                CedentCommissionPct = 20m,
                CedentPremiumReference = "REF-8892-AXA",
                IsFronting = false,
                MemoText = "Internal underwriting notes.",
                CoverageSelection = new CoverageSelectionRequestDto
                {
                    MainCoverId = 1,
                    SectionIds = [2, 2, 3],
                    ExtensionIds = [7]
                }
            };
        }

        private static UserProfileResponseDto CreateFrontOfficerProfile()
        {
            return new UserProfileResponseDto
            {
                Id = "user-1",
                FullName = "Front Officer",
                Username = "front.officer",
                Email = "front.officer@iris.local",
                Department = ContractConstants.FrontOfficerDepartment,
                Position = ContractConstants.FrontOfficerPosition,
                Roles = [IdentityRoles.Employee]
            };
        }

        private static void SeedDraft(FakeContractRepository contractRepository, string ownerUserId = "user-1", string status = "DRAFT")
        {
            contractRepository.Contracts.Add(new Contract
            {
                IdContract = 1,
                Umr = "FAC-001",
                Name = "Existing Draft",
                IdCounterparty = 10,
                IdCurrency = 2,
                IdUser = ownerUserId,
                TypeOfBusiness = ContractConstants.FacultativeType,
                Status = status,
                CreationDate = new DateOnly(2026, 8, 19),
                ReceptionDate = new DateOnly(2026, 8, 19),
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                GrossPremium = 500000m,
                CedentCommission = 10m,
                NetPremiumCedent = 450000m,
                CedentPremiumReference = "REF-1",
                IsFronting = false,
                MemoText = "Memo",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = ownerUserId,
                IsCurrent = true,
                ContractFac = new ContractFac
                {
                    IdContractFac = 1,
                    IdContract = 1,
                    SumInsured = 15000000m,
                    Deductible = 500000m,
                    PremiumRate = 0.045m
                }
            });

            contractRepository.ContractFacs.Add(contractRepository.Contracts[0].ContractFac);
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

        private sealed class FakeCoverageSelectionService : ICoverageSelectionService
        {
            public List<int> CreatedContractIds { get; } = [];

            public List<string> CreatedByUserIds { get; } = [];

            public bool FailCreate { get; set; }

            public Task<ServiceResult<CoverageSelectionPlan>> ValidateAsync(
                CoverageSelectionRequestDto? request,
                CancellationToken cancellationToken = default)
            {
                if (request is null || request.MainCoverId <= 0)
                {
                    return Task.FromResult(ServiceResult<CoverageSelectionPlan>.Failure(
                        "Validation failed.",
                        ["coverageSelection.mainCoverId is required."],
                        StatusCodes.Status400BadRequest));
                }

                var sectionIds = request.SectionIds.Distinct().ToArray();
                var extensionIds = request.ExtensionIds.Distinct().ToArray();
                return Task.FromResult(ServiceResult<CoverageSelectionPlan>.Success(new CoverageSelectionPlan
                {
                    MainCoverId = request.MainCoverId,
                    SectionIds = sectionIds,
                    ExtensionIds = extensionIds,
                    SlipTemplateIdsByCoverageItemId = new Dictionary<int, int>
                    {
                        [request.MainCoverId] = 10,
                        [extensionIds.SingleOrDefault()] = 11
                    }.Where(item => item.Key > 0).ToDictionary(item => item.Key, item => item.Value)
                }));
            }

            public Task CreateSelectionsAsync(
                int contractId,
                CoverageSelectionPlan plan,
                string userId,
                DateTime now,
                CancellationToken cancellationToken = default)
            {
                if (FailCreate)
                {
                    throw new DbUpdateException("Failed to save coverage selections.", new Exception("Simulated failure"));
                }

                CreatedContractIds.Add(contractId);
                CreatedByUserIds.Add(userId);
                return Task.CompletedTask;
            }

            public Task ReplaceSelectionsAsync(
                int contractId,
                CoverageSelectionPlan plan,
                string userId,
                DateTime now,
                CancellationToken cancellationToken = default)
            {
                CreatedContractIds.Add(contractId);
                CreatedByUserIds.Add(userId);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeReferenceDataRepository : IReferenceDataRepository
        {
            public List<Counterparty> Counterparties { get; } =
            [
                new Counterparty
                {
                    Id = 10,
                    Name = "AXA Cedant",
                    Type = " Cedent ",
                    Country = "United Kingdom",
                    City = "London",
                    IsActive = true,
                    Status = "Active"
                },
                new Counterparty
                {
                    Id = 11,
                    Name = "Broker Co",
                    Type = "Broker",
                    Country = "France",
                    City = "Paris",
                    IsActive = true,
                    Status = "Active"
                }
            ];

            public List<Currency> Currencies { get; } =
            [
                new Currency
                {
                    IdCurrency = 2,
                    CurrencyCode = "USD",
                    CurrencySymbol = "$",
                    Name = "US Dollar",
                    DecimalPlaces = 2,
                    IsDefault = true,
                    IsActive = true
                },
                new Currency
                {
                    IdCurrency = 3,
                    CurrencyCode = "EUR",
                    CurrencySymbol = "€",
                    Name = "Euro",
                    DecimalPlaces = 2,
                    IsDefault = false,
                    IsActive = true
                }
            ];

            public Task<IReadOnlyList<Currency>> ListActiveCurrenciesAsync(CancellationToken cancellationToken = default)
            {
                IReadOnlyList<Currency> items = Currencies
                    .Where(currency => currency.IsActive)
                    .OrderByDescending(currency => currency.IsDefault)
                    .ThenBy(currency => currency.Name)
                    .ToList();

                return Task.FromResult(items);
            }

            public Task<(IReadOnlyList<Counterparty> Items, int TotalCount)> SearchActiveCedentsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
            {
                var normalizedSearch = search?.Trim();
                var query = Counterparties
                    .Where(counterparty =>
                        counterparty.IsActive &&
                        !string.IsNullOrWhiteSpace(counterparty.Type) &&
                        string.Equals(counterparty.Type.Trim(), "Cedent", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    query = query.Where(counterparty =>
                        counterparty.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(counterparty.Country) && counterparty.Country.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(counterparty.City) && counterparty.City.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)));
                }

                var items = query
                    .OrderBy(counterparty => counterparty.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Task.FromResult(((IReadOnlyList<Counterparty>)items, query.Count()));
            }

            public Task<Counterparty?> GetActiveCedentByIdAsync(int counterpartyId, CancellationToken cancellationToken = default)
            {
                Counterparty? counterparty = Counterparties.SingleOrDefault(item =>
                    item.Id == counterpartyId &&
                    item.IsActive &&
                    !string.IsNullOrWhiteSpace(item.Type) &&
                    string.Equals(item.Type.Trim(), "Cedent", StringComparison.OrdinalIgnoreCase));

                return Task.FromResult(counterparty);
            }

            public Task<Currency?> GetActiveCurrencyByIdAsync(int currencyId, CancellationToken cancellationToken = default)
            {
                Currency? currency = Currencies.SingleOrDefault(item => item.IdCurrency == currencyId && item.IsActive);
                return Task.FromResult(currency);
            }
        }

        private sealed class FakeContractRepository : IContractRepository
        {
            private readonly FakeReferenceDataRepository _referenceDataRepository;
            private int _nextContractId = 1;
            private int _nextContractFacId = 1;

            public FakeContractRepository(FakeReferenceDataRepository referenceDataRepository)
            {
                _referenceDataRepository = referenceDataRepository;
            }

            public List<Contract> Contracts { get; } = [];

            public List<ContractFac> ContractFacs { get; } = [];

            public bool FailOnNextContractFacSave { get; set; }

            public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IDbContextTransaction>(new FakeDbContextTransaction(this));
            }

            public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default)
            {
                var exists = Contracts.Any(contract =>
                    !string.IsNullOrWhiteSpace(contract.Umr) &&
                    string.Equals(contract.Umr, umr, StringComparison.OrdinalIgnoreCase) &&
                    (!excludedContractId.HasValue || contract.IdContract != excludedContractId.Value));

                return Task.FromResult(exists);
            }

            public Task AddAsync(Contract contract, CancellationToken cancellationToken = default)
            {
                Contracts.Add(contract);
                return Task.CompletedTask;
            }

            public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default)
            {
                ContractFacs.Add(contractFac);
                return Task.CompletedTask;
            }

            public void Update(Contract contract)
            {
            }

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                if (FailOnNextContractFacSave && ContractFacs.Any(contractFac => contractFac.IdContractFac == 0))
                {
                    FailOnNextContractFacSave = false;
                    throw new DbUpdateException("Failed to save contract fac.", new Exception("Simulated failure"));
                }

                foreach (var contract in Contracts.Where(contract => contract.IdContract == 0))
                {
                    contract.IdContract = _nextContractId++;
                }

                foreach (var contractFac in ContractFacs.Where(contractFac => contractFac.IdContractFac == 0))
                {
                    contractFac.IdContractFac = _nextContractFacId++;
                }

                foreach (var contract in Contracts)
                {
                    contract.ContractFac = ContractFacs.SingleOrDefault(contractFac => contractFac.IdContract == contract.IdContract);
                }

                return Task.FromResult(1);
            }

            public Task<Contract?> GetFacultativeContractByIdAsync(int contractId, bool asNoTracking = false, CancellationToken cancellationToken = default)
            {
                var contract = Contracts.SingleOrDefault(item =>
                    item.IdContract == contractId &&
                    string.Equals(item.TypeOfBusiness, ContractConstants.FacultativeType, StringComparison.OrdinalIgnoreCase));

                if (contract is null)
                {
                    return Task.FromResult<Contract?>(null);
                }

                var materialized = CloneContract(contract);
                materialized.ContractFac = ContractFacs.SingleOrDefault(contractFac => contractFac.IdContract == contractId) is { } contractFac
                    ? CloneContractFac(contractFac)
                    : null;
                materialized.IdCounterpartyNavigation = _referenceDataRepository.Counterparties.Single(counterparty => counterparty.Id == contract.IdCounterparty);
                materialized.IdCurrencyNavigation = _referenceDataRepository.Currencies.Single(currency => currency.IdCurrency == contract.IdCurrency);

                if (!asNoTracking)
                {
                    contract.ContractFac = materialized.ContractFac;
                    contract.IdCounterpartyNavigation = materialized.IdCounterpartyNavigation;
                    contract.IdCurrencyNavigation = materialized.IdCurrencyNavigation;
                    return Task.FromResult<Contract?>(contract);
                }

                return Task.FromResult<Contract?>(materialized);
            }

            public Task<(IReadOnlyList<ContractListItemDto> Items, int TotalCount)> ListAsync(
                ContractListQueryDto query,
                string userId,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(((IReadOnlyList<ContractListItemDto>)Array.Empty<ContractListItemDto>(), 0));
            }

            public Task<ContractDashboardData> GetDashboardDataAsync(
                string userId,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new ContractDashboardData());
            }

            public Task<Contract?> GetAccessibleContractByIdAsync(
                int contractId,
                string userId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task MarkDraftReadyForPlacementAsync(
                int contractId,
                string userId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<SlipTemplate>> ListActiveCurrentSlipTemplatesForContractAsync(
                int contractId,
                string userId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public RepositorySnapshot CaptureSnapshot()
            {
                return new RepositorySnapshot(
                    Contracts.Select(CloneContract).ToList(),
                    ContractFacs.Select(CloneContractFac).ToList(),
                    _nextContractId,
                    _nextContractFacId);
            }

            public void RestoreSnapshot(RepositorySnapshot snapshot)
            {
                Contracts.Clear();
                Contracts.AddRange(snapshot.Contracts.Select(CloneContract));

                ContractFacs.Clear();
                ContractFacs.AddRange(snapshot.ContractFacs.Select(CloneContractFac));

                _nextContractId = snapshot.NextContractId;
                _nextContractFacId = snapshot.NextContractFacId;
            }

            private static Contract CloneContract(Contract source)
            {
                return new Contract
                {
                    IdContract = source.IdContract,
                    IdPreviousContract = source.IdPreviousContract,
                    IsFronting = source.IsFronting,
                    IdCounterparty = source.IdCounterparty,
                    IdCurrency = source.IdCurrency,
                    IdUser = source.IdUser,
                    TypeOfBusiness = source.TypeOfBusiness,
                    Name = source.Name,
                    CreationDate = source.CreationDate,
                    ReceptionDate = source.ReceptionDate,
                    InceptionDate = source.InceptionDate,
                    ExpiryDate = source.ExpiryDate,
                    Status = source.Status,
                    Fees = source.Fees,
                    MemoText = source.MemoText,
                    UnderwritingYearBasis = source.UnderwritingYearBasis,
                    IsCurrent = source.IsCurrent,
                    CreatedAt = source.CreatedAt,
                    CreatedBy = source.CreatedBy,
                    UpdatedAt = source.UpdatedAt,
                    UpdatedBy = source.UpdatedBy,
                    GrossPremium = source.GrossPremium,
                    Flag = source.Flag,
                    CedentCommission = source.CedentCommission,
                    NetPremiumCedent = source.NetPremiumCedent,
                    Umr = source.Umr,
                    CedentPremiumReference = source.CedentPremiumReference,
                    FrontingReference = source.FrontingReference,
                    WrittenSharePctCedent = source.WrittenSharePctCedent,
                    SignedSharePctCedent = source.SignedSharePctCedent
                };
            }

            private static ContractFac CloneContractFac(ContractFac source)
            {
                return new ContractFac
                {
                    IdContractFac = source.IdContractFac,
                    IdContract = source.IdContract,
                    SumInsured = source.SumInsured,
                    Deductible = source.Deductible,
                    PremiumRate = source.PremiumRate
                };
            }

            public sealed record RepositorySnapshot(
                List<Contract> Contracts,
                List<ContractFac> ContractFacs,
                int NextContractId,
                int NextContractFacId);
        }

        private sealed class FakeDbContextTransaction : IDbContextTransaction
        {
            private readonly FakeContractRepository _repository;
            private readonly FakeContractRepository.RepositorySnapshot _snapshot;
            private bool _committed;

            public FakeDbContextTransaction(FakeContractRepository repository)
            {
                _repository = repository;
                _snapshot = repository.CaptureSnapshot();
            }

            public Guid TransactionId { get; } = Guid.NewGuid();

            public bool SupportsSavepoints => false;

            public void Commit()
            {
                _committed = true;
            }

            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                _committed = true;
                return Task.CompletedTask;
            }

            public void Rollback()
            {
                _repository.RestoreSnapshot(_snapshot);
            }

            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                _repository.RestoreSnapshot(_snapshot);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                if (!_committed)
                {
                    _repository.RestoreSnapshot(_snapshot);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void CreateSavepoint(string name) => throw new NotSupportedException();

            public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public void RollbackToSavepoint(string name) => throw new NotSupportedException();

            public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public void ReleaseSavepoint(string name) => throw new NotSupportedException();

            public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public IDbTransaction GetDbTransaction() => throw new NotSupportedException();
        }
    }
}
