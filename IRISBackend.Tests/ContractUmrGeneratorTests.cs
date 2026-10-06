using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ContractUmrGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_UsesTheInceptionYearTypeAndMainCoverScope()
    {
        var repository = new FakeContractRepository();
        var generator = new ContractUmrGenerator(repository);

        var first = await generator.GenerateAsync("Facultative", new DateOnly(2026, 9, 1), Plan("PD"));
        var second = await generator.GenerateAsync("Facultative", new DateOnly(2026, 10, 1), Plan("PD"));
        var differentCover = await generator.GenerateAsync("Facultative", new DateOnly(2026, 10, 1), Plan("FR"));
        var differentYear = await generator.GenerateAsync("Facultative", new DateOnly(2027, 1, 1), Plan("PD"));

        Assert.Equal("IT26-F-PD-0001", first.Value);
        Assert.Equal("IT26-F-PD-0002", second.Value);
        Assert.Equal("IT26-F-FR-0001", differentCover.Value);
        Assert.Equal("IT27-F-PD-0001", differentYear.Value);
    }

    [Fact]
    public async Task GenerateAsync_ConcurrentCallsProduceUniqueSequences()
    {
        var repository = new FakeContractRepository();
        var generator = new ContractUmrGenerator(repository);

        var results = await Task.WhenAll(Enumerable.Range(0, 25)
            .Select(_ => generator.GenerateAsync("Facultative", new DateOnly(2026, 9, 1), Plan("PD"))));

        Assert.Equal(25, results.Select(result => result.Value).Distinct().Count());
        Assert.Contains("IT26-F-PD-0001", results.Select(result => result.Value));
        Assert.Contains("IT26-F-PD-0025", results.Select(result => result.Value));
    }

    [Fact]
    public async Task GenerateAsync_WhenSequenceExceedsFourDigitsReturnsConflict()
    {
        var repository = new FakeContractRepository { NextSequence = 10000 };
        var generator = new ContractUmrGenerator(repository);

        var result = await generator.GenerateAsync("Facultative", new DateOnly(2026, 9, 1), Plan("PD"));

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("exceeded 9999", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnknownTypeAndUnsafeCoverCode()
    {
        var generator = new ContractUmrGenerator(new FakeContractRepository());

        var unknownType = await generator.GenerateAsync("Quota Share", new DateOnly(2026, 9, 1), Plan("PD"));
        var unsafeCode = await generator.GenerateAsync("Facultative", new DateOnly(2026, 9, 1), Plan("P-D"));

        Assert.Equal(400, unknownType.StatusCode);
        Assert.Equal(400, unsafeCode.StatusCode);
    }

    private static CoverageSelectionPlan Plan(string code) => new()
    {
        MainCoverId = 1,
        MainCoverCode = code
    };

    private sealed class FakeContractRepository : IContractRepository
    {
        private readonly object _sync = new();
        private readonly Dictionary<(int Year, string Type, string Cover), int> _sequences = new();

        public int NextSequence
        {
            init => _sequences[(2026, "F", "PD")] = value - 1;
        }

        public Task<int> AllocateUmrSequenceAsync(int inceptionYear, string contractTypeCode, string mainCoverCode, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var key = (inceptionYear, contractTypeCode, mainCoverCode);
                _sequences[key] = _sequences.GetValueOrDefault(key) + 1;
                return Task.FromResult(_sequences[key]);
            }
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Contract contract, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(Contract contract) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Contract?> GetFacultativeContractByIdAsync(int contractId, bool asNoTracking = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Contract?> GetAccessibleContractByIdAsync(int contractId, string userId, bool includeAllContracts = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkDraftReadyForPlacementAsync(int contractId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SlipTemplate>> ListActiveCurrentSlipTemplatesForContractAsync(int contractId, string userId, bool includeAllContracts = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyList<ContractListItemDto> Items, int TotalCount)> ListAsync(ContractListQueryDto query, string userId, bool includeAllContracts = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContractDashboardData> GetDashboardDataAsync(string userId, bool includeAllContracts = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
