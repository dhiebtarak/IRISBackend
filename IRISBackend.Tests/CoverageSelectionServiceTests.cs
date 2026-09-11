using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IRISBackend.Tests
{
    public sealed class CoverageSelectionServiceTests
    {
        [Fact]
        public async Task ValidateAsync_WhenSelectionIsValid_NormalizesDuplicateIdsAndAllowsMissingSlips()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageSelectionService(repository);

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                MainCoverId = 1,
                SectionIds = [2, 2],
                ExtensionIds = [4, 4]
            });

            Assert.True(result.Succeeded);
            Assert.Equal([2], result.Value!.SectionIds);
            Assert.Equal([4], result.Value.ExtensionIds);
            Assert.True(result.Value.SlipTemplateIdsByCoverageItemId.ContainsKey(1));
            Assert.False(result.Value.SlipTemplateIdsByCoverageItemId.ContainsKey(4));
        }

        [Fact]
        public async Task ValidateAsync_AllowsMultipleMainCoversAndKeepsPrimaryForUmr()
        {
            var repository = new FakeCoverageRepository();
            repository.Items[6] = new CoverageItemLookup(6, CoverageConstants.MainCoverLevel, "PVT", "Political Violence", true);
            var service = new CoverageSelectionService(repository);

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                PrimaryMainCoverId = 1,
                CoverageIds = [1, 6],
                SectionIds = [2]
            });

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Value!.PrimaryMainCoverId);
            Assert.Equal([1, 6], result.Value.MainCoverIds);
            Assert.Equal("PD", result.Value.MainCoverCode);
        }

        [Fact]
        public async Task ValidateAsync_RejectsDuplicateCoverageIds()
        {
            var service = new CoverageSelectionService(new FakeCoverageRepository());

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                PrimaryMainCoverId = 1,
                CoverageIds = [1, 1]
            });

            Assert.False(result.Succeeded);
            Assert.Contains(result.Error!.Errors, error => error.Contains("duplicates", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ValidateAsync_RejectsPrimaryNotIncludedInCoverageIds()
        {
            var service = new CoverageSelectionService(new FakeCoverageRepository());

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                PrimaryMainCoverId = 1,
                CoverageIds = [6]
            });

            Assert.False(result.Succeeded);
            Assert.Contains(result.Error!.Errors, error => error.Contains("included in coverageIds", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ValidateAsync_WhenSectionIsNotRelatedToMainCover_ReturnsBadRequest()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageSelectionService(repository);

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                MainCoverId = 1,
                SectionIds = [3]
            });

            Assert.False(result.Succeeded);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Contains(result.Error!.Errors, error => error.Contains("section 3", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ValidateAsync_WhenCoverageItemIsInactive_ReturnsBadRequest()
        {
            var repository = new FakeCoverageRepository();
            repository.Items[2] = repository.Items[2] with { IsActive = false };
            var service = new CoverageSelectionService(repository);

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                MainCoverId = 1,
                SectionIds = [2]
            });

            Assert.False(result.Succeeded);
            Assert.Contains(result.Error!.Errors, error => error.Contains("section 2", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ValidateAsync_WhenMandatorySectionIsMissing_ReturnsBadRequest()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageSelectionService(repository);

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                MainCoverId = 1,
                SectionIds = [],
                ExtensionIds = []
            });

            Assert.False(result.Succeeded);
            Assert.Contains(result.Error!.Errors, error => error.Contains("Mandatory section 2", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ValidateAsync_WhenExtensionIsRelatedToSelectedSection_Succeeds()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageSelectionService(repository);

            var result = await service.ValidateAsync(new CoverageSelectionRequestDto
            {
                MainCoverId = 1,
                SectionIds = [2],
                ExtensionIds = [5]
            });

            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task CreateSelectionsAsync_SavesMainCoverThenSectionsThenExtensions()
        {
            var repository = new FakeCoverageRepository();
            var service = new CoverageSelectionService(repository);
            var plan = new CoverageSelectionPlan
            {
                MainCoverId = 1,
                SectionIds = [2],
                ExtensionIds = [4],
                SlipTemplateIdsByCoverageItemId = new Dictionary<int, int> { [1] = 10 }
            };

            await service.CreateSelectionsAsync(12, plan, "user-1", DateTime.UtcNow);

            Assert.Equal(
                ["add:1", "save", "add:2", "save", "add:4", "save"],
                repository.Events);
        }

        [Fact]
        public async Task ReplaceSelectionsAsync_WhenMainCoverChanges_RemovesChildrenBeforeMainAndInsertsInTriggerOrder()
        {
            var repository = new FakeCoverageRepository();
            repository.ExistingSelections.AddRange(
            [
                CreateSelection(12, 9, CoverageConstants.MainCoverLevel),
                CreateSelection(12, 3, CoverageConstants.SectionLevel),
                CreateSelection(12, 5, CoverageConstants.ExtensionLevel)
            ]);
            var service = new CoverageSelectionService(repository);
            var plan = new CoverageSelectionPlan
            {
                MainCoverId = 1,
                SectionIds = [2],
                ExtensionIds = [4]
            };

            await service.ReplaceSelectionsAsync(12, plan, "user-1", DateTime.UtcNow);

            Assert.Equal(
                ["remove:5", "remove:3", "save", "remove:9", "save", "add:1", "save", "add:2", "save", "add:4", "save"],
                repository.Events);
        }

        private static ContractCoverageSelection CreateSelection(int contractId, int coverageItemId, string level)
        {
            return new ContractCoverageSelection
            {
                IdContract = contractId,
                IdCoverageItem = coverageItemId,
                IdCoverageItemNavigation = new CoverageItem
                {
                    IdCoverageItem = coverageItemId,
                    CoverageLevel = level,
                    CoverageCode = $"C{coverageItemId}",
                    CoverageName = $"Coverage {coverageItemId}",
                    IsActive = true
                }
            };
        }

        private sealed class FakeCoverageRepository : ICoverageRepository
        {
            public Dictionary<int, CoverageItemLookup> Items { get; } = new()
            {
                [1] = new CoverageItemLookup(1, CoverageConstants.MainCoverLevel, "PD", "Property Damage", true),
                [2] = new CoverageItemLookup(2, CoverageConstants.SectionLevel, "FIRE", "Fire", true),
                [3] = new CoverageItemLookup(3, CoverageConstants.SectionLevel, "THEFT", "Theft", true),
                [4] = new CoverageItemLookup(4, CoverageConstants.ExtensionLevel, "EQ", "Earthquake", true),
                [5] = new CoverageItemLookup(5, CoverageConstants.ExtensionLevel, "FLOOD", "Flood", true)
            };

            private readonly List<CoverageRelationLookup> _relations =
            [
                new CoverageRelationLookup(1, 2, CoverageConstants.HasSectionRelation, true, true),
                new CoverageRelationLookup(1, 4, CoverageConstants.HasExtensionRelation, false, true),
                new CoverageRelationLookup(2, 5, CoverageConstants.HasExtensionRelation, false, true)
            ];

            public List<ContractCoverageSelection> ExistingSelections { get; } = [];

            public List<string> Events { get; } = [];

            public Task<IReadOnlyList<CoverageCatalogItemDto>> ListMainCoversAsync(CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageCatalogItemDto> result =
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

                return Task.FromResult(result);
            }

            public Task<IReadOnlyList<CoverageCatalogItemDto>> ListChildrenAsync(
                int parentCoverageItemId,
                string requestedLevel,
                string relationType,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageCatalogItemDto> result = [];
                return Task.FromResult(result);
            }

            public Task<IReadOnlyList<CoverageItemLookup>> GetCoverageItemsByIdsAsync(
                IReadOnlyCollection<int> coverageItemIds,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageItemLookup> result = coverageItemIds
                    .Where(Items.ContainsKey)
                    .Select(id => Items[id])
                    .ToList();

                return Task.FromResult(result);
            }

            public Task<IReadOnlyList<CoverageRelationLookup>> GetActiveRelationsForParentsAsync(
                IReadOnlyCollection<int> parentCoverageItemIds,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CoverageRelationLookup> result = _relations
                    .Where(relation => parentCoverageItemIds.Contains(relation.ParentId) && relation.IsActive)
                    .ToList();

                return Task.FromResult(result);
            }

            public Task<IReadOnlyDictionary<int, int>> GetActiveCurrentSlipIdsAsync(
                IReadOnlyCollection<int> coverageItemIds,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyDictionary<int, int> result = coverageItemIds.Contains(1)
                    ? new Dictionary<int, int> { [1] = 10 }
                    : new Dictionary<int, int>();

                return Task.FromResult(result);
            }

            public Task<IReadOnlyList<ContractCoverageSelection>> GetSelectionsForContractAsync(
                int contractId,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ContractCoverageSelection> result = ExistingSelections
                    .Where(selection => selection.IdContract == contractId)
                    .ToList();

                return Task.FromResult(result);
            }

            public Task AddSelectionsAsync(IEnumerable<ContractCoverageSelection> selections, CancellationToken cancellationToken = default)
            {
                var materialized = selections.ToList();
                ExistingSelections.AddRange(materialized);
                Events.Add("add:" + string.Join(",", materialized.Select(selection => selection.IdCoverageItem)));
                return Task.CompletedTask;
            }

            public void RemoveSelections(IEnumerable<ContractCoverageSelection> selections)
            {
                var materialized = selections.ToList();
                foreach (var selection in materialized)
                {
                    ExistingSelections.Remove(selection);
                }

                Events.AddRange(materialized.Select(selection => $"remove:{selection.IdCoverageItem}"));
            }

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                Events.Add("save");
                return Task.FromResult(1);
            }
        }
    }
}
