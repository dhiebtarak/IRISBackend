using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IRISBackend.Tests
{
    public sealed class CoverageCatalogServiceTests
    {
        [Fact]
        public async Task GetMainCoversAsync_ReturnsRepositoryMainCovers()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageCatalogService(repository);

            var result = await service.GetMainCoversAsync();

            Assert.True(result.Succeeded);
            Assert.Single(result.Value!);
            Assert.Equal(CoverageConstants.MainCoverLevel, result.Value!.Single().Level);
        }

        [Fact]
        public async Task GetChildrenAsync_WhenLevelIsInvalid_ReturnsBadRequest()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageCatalogService(repository);

            var result = await service.GetChildrenAsync(1, "CLAUSE");

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Contains(result.Error!.Errors, error => error.Contains("SECTION or EXTENSION", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GetChildrenAsync_WhenLevelIsSection_UsesHasSectionRelation()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageCatalogService(repository);

            var result = await service.GetChildrenAsync(1, " section ");

            Assert.True(result.Succeeded);
            Assert.Equal(CoverageConstants.SectionLevel, repository.LastRequestedLevel);
            Assert.Equal(CoverageConstants.HasSectionRelation, repository.LastRelationType);
        }

        private sealed class FakeCoverageRepository : ICoverageRepository
        {
            public string? LastRequestedLevel { get; private set; }

            public string? LastRelationType { get; private set; }

            public Task<IReadOnlyList<CoverageCatalogItemDto>> ListMainCoversAsync(CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageCatalogItemDto> items =
                [
                    new CoverageCatalogItemDto
                    {
                        Id = 1,
                        Code = "PD",
                        Name = "Property Damage",
                        Level = CoverageConstants.MainCoverLevel,
                        HasSlip = true
                    }
                ];

                return Task.FromResult(items);
            }

            public Task<IReadOnlyList<CoverageCatalogItemDto>> ListChildrenAsync(
                int parentCoverageItemId,
                string requestedLevel,
                string relationType,
                CancellationToken cancellationToken = default)
            {
                LastRequestedLevel = requestedLevel;
                LastRelationType = relationType;
                IReadOnlyList<CoverageCatalogItemDto> items = [];
                return Task.FromResult(items);
            }

            public Task<IReadOnlyList<CoverageItemLookup>> GetCoverageItemsByIdsAsync(IReadOnlyCollection<int> coverageItemIds, CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageItemLookup> items = [];
                return Task.FromResult(items);
            }

            public Task<IReadOnlyList<CoverageRelationLookup>> GetActiveRelationsForParentsAsync(IReadOnlyCollection<int> parentCoverageItemIds, CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageRelationLookup> items = [];
                return Task.FromResult(items);
            }

            public Task<IReadOnlyDictionary<int, int>> GetActiveCurrentSlipIdsAsync(IReadOnlyCollection<int> coverageItemIds, CancellationToken cancellationToken = default)
            {
                IReadOnlyDictionary<int, int> items = new Dictionary<int, int>();
                return Task.FromResult(items);
            }

            public Task<IReadOnlyList<ContractCoverageSelection>> GetSelectionsForContractAsync(int contractId, CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ContractCoverageSelection> items = [];
                return Task.FromResult(items);
            }

            public Task AddSelectionsAsync(IEnumerable<ContractCoverageSelection> selections, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public void RemoveSelections(IEnumerable<ContractCoverageSelection> selections)
            {
            }

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(1);
            }
        }
    }
}
