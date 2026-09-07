using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;

namespace IRISBackend.Services
{
    public sealed class CoverageSelectionService : ICoverageSelectionService
    {
        private readonly ICoverageRepository _coverageRepository;

        public CoverageSelectionService(ICoverageRepository coverageRepository)
        {
            _coverageRepository = coverageRepository;
        }

        public async Task<ServiceResult<CoverageSelectionPlan>> ValidateAsync(
            CoverageSelectionRequestDto? request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                return ValidationFailure("coverageSelection is required.");
            }

            var mainCoverId = request.MainCoverId;
            var sectionIds = NormalizeIds(request.SectionIds);
            var extensionIds = NormalizeIds(request.ExtensionIds);
            var allRequestedIds = new[] { mainCoverId }.Concat(sectionIds).Concat(extensionIds).Where(id => id > 0).Distinct().ToArray();
            var errors = new List<string>();

            if (mainCoverId <= 0)
            {
                errors.Add("coverageSelection.mainCoverId is required.");
            }

            var items = await _coverageRepository.GetCoverageItemsByIdsAsync(allRequestedIds, cancellationToken);
            var itemsById = items.ToDictionary(item => item.Id);
            var mainCover = itemsById.GetValueOrDefault(mainCoverId);

            if (mainCover is null || !mainCover.IsActive || !IsLevel(mainCover.Level, CoverageConstants.MainCoverLevel))
            {
                errors.Add("The selected main cover does not exist, is inactive, or is not a MAIN_COVER.");
            }

            foreach (var sectionId in sectionIds)
            {
                if (!itemsById.TryGetValue(sectionId, out var section) ||
                    !section.IsActive ||
                    !IsLevel(section.Level, CoverageConstants.SectionLevel))
                {
                    errors.Add($"Selected section {sectionId} does not exist, is inactive, or is not a SECTION.");
                }
            }

            foreach (var extensionId in extensionIds)
            {
                if (!itemsById.TryGetValue(extensionId, out var extension) ||
                    !extension.IsActive ||
                    !IsLevel(extension.Level, CoverageConstants.ExtensionLevel))
                {
                    errors.Add($"Selected extension {extensionId} does not exist, is inactive, or is not an EXTENSION.");
                }
            }

            var relationParentIds = new[] { mainCoverId }.Concat(sectionIds).Where(id => id > 0).Distinct().ToArray();
            var relations = await _coverageRepository.GetActiveRelationsForParentsAsync(relationParentIds, cancellationToken);

            ValidateSections(mainCoverId, sectionIds, relations, errors);
            ValidateExtensions(mainCoverId, sectionIds, extensionIds, relations, errors);
            ValidateMandatoryChildren(mainCoverId, sectionIds, extensionIds, relations, errors);

            if (errors.Count > 0)
            {
                return ValidationFailure(errors);
            }

            var slipTemplateIds = await _coverageRepository.GetActiveCurrentSlipIdsAsync(allRequestedIds, cancellationToken);
            return ServiceResult<CoverageSelectionPlan>.Success(new CoverageSelectionPlan
            {
                MainCoverId = mainCoverId,
                SectionIds = sectionIds,
                ExtensionIds = extensionIds,
                SlipTemplateIdsByCoverageItemId = slipTemplateIds
            });
        }

        public async Task CreateSelectionsAsync(
            int contractId,
            CoverageSelectionPlan plan,
            string userId,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            await AddSelectionsByLevelAsync(contractId, [plan.MainCoverId], plan, userId, now, cancellationToken);
            await AddSelectionsByLevelAsync(contractId, plan.SectionIds, plan, userId, now, cancellationToken);
            await AddSelectionsByLevelAsync(contractId, plan.ExtensionIds, plan, userId, now, cancellationToken);
        }

        public async Task ReplaceSelectionsAsync(
            int contractId,
            CoverageSelectionPlan plan,
            string userId,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            var currentSelections = await _coverageRepository.GetSelectionsForContractAsync(contractId, cancellationToken);
            var currentMain = currentSelections.SingleOrDefault(selection =>
                IsLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.MainCoverLevel));

            var currentSections = currentSelections
                .Where(selection => IsLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.SectionLevel))
                .ToList();

            var currentExtensions = currentSelections
                .Where(selection => IsLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.ExtensionLevel))
                .ToList();

            var mainChanged = currentMain?.IdCoverageItem != plan.MainCoverId;
            var extensionsToRemove = mainChanged
                ? currentExtensions
                : currentExtensions.Where(selection => !plan.ExtensionIds.Contains(selection.IdCoverageItem)).ToList();
            var sectionsToRemove = mainChanged
                ? currentSections
                : currentSections.Where(selection => !plan.SectionIds.Contains(selection.IdCoverageItem)).ToList();

            _coverageRepository.RemoveSelections(extensionsToRemove);
            _coverageRepository.RemoveSelections(sectionsToRemove);
            await _coverageRepository.SaveChangesAsync(cancellationToken);

            if (mainChanged)
            {
                if (currentMain is not null)
                {
                    _coverageRepository.RemoveSelections([currentMain]);
                    await _coverageRepository.SaveChangesAsync(cancellationToken);
                }

                await AddSelectionsByLevelAsync(contractId, [plan.MainCoverId], plan, userId, now, cancellationToken);
                await AddSelectionsByLevelAsync(contractId, plan.SectionIds, plan, userId, now, cancellationToken);
                await AddSelectionsByLevelAsync(contractId, plan.ExtensionIds, plan, userId, now, cancellationToken);
                return;
            }

            var currentSectionIds = currentSections.Select(selection => selection.IdCoverageItem).ToHashSet();
            var currentExtensionIds = currentExtensions.Select(selection => selection.IdCoverageItem).ToHashSet();
            var sectionsToAdd = plan.SectionIds.Where(id => !currentSectionIds.Contains(id)).ToArray();
            var extensionsToAdd = plan.ExtensionIds.Where(id => !currentExtensionIds.Contains(id)).ToArray();

            await AddSelectionsByLevelAsync(contractId, sectionsToAdd, plan, userId, now, cancellationToken);
            await AddSelectionsByLevelAsync(contractId, extensionsToAdd, plan, userId, now, cancellationToken);
        }

        private async Task AddSelectionsByLevelAsync(
            int contractId,
            IReadOnlyCollection<int> coverageItemIds,
            CoverageSelectionPlan plan,
            string userId,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (coverageItemIds.Count == 0)
            {
                return;
            }

            var selections = coverageItemIds.Select(coverageItemId => new ContractCoverageSelection
            {
                IdContract = contractId,
                IdCoverageItem = coverageItemId,
                IdSlipTemplate = plan.SlipTemplateIdsByCoverageItemId.GetValueOrDefault(coverageItemId) == 0
                    ? null
                    : plan.SlipTemplateIdsByCoverageItemId[coverageItemId],
                CreatedAt = now,
                CreatedBy = userId
            });

            await _coverageRepository.AddSelectionsAsync(selections, cancellationToken);
            await _coverageRepository.SaveChangesAsync(cancellationToken);
        }

        private static void ValidateSections(
            int mainCoverId,
            IReadOnlyCollection<int> sectionIds,
            IReadOnlyCollection<CoverageRelationLookup> relations,
            List<string> errors)
        {
            var validSectionIds = relations
                .Where(relation =>
                    relation.ParentId == mainCoverId &&
                    IsRelation(relation.RelationType, CoverageConstants.HasSectionRelation))
                .Select(relation => relation.ChildId)
                .ToHashSet();

            foreach (var sectionId in sectionIds.Where(sectionId => !validSectionIds.Contains(sectionId)))
            {
                errors.Add($"Selected section {sectionId} is not related to the selected main cover.");
            }
        }

        private static void ValidateExtensions(
            int mainCoverId,
            IReadOnlyCollection<int> sectionIds,
            IReadOnlyCollection<int> extensionIds,
            IReadOnlyCollection<CoverageRelationLookup> relations,
            List<string> errors)
        {
            var allowedParentIds = new[] { mainCoverId }.Concat(sectionIds).ToHashSet();
            var validExtensionIds = relations
                .Where(relation =>
                    allowedParentIds.Contains(relation.ParentId) &&
                    IsRelation(relation.RelationType, CoverageConstants.HasExtensionRelation))
                .Select(relation => relation.ChildId)
                .ToHashSet();

            foreach (var extensionId in extensionIds.Where(extensionId => !validExtensionIds.Contains(extensionId)))
            {
                errors.Add($"Selected extension {extensionId} is not related to the selected main cover or sections.");
            }
        }

        private static void ValidateMandatoryChildren(
            int mainCoverId,
            IReadOnlyCollection<int> sectionIds,
            IReadOnlyCollection<int> extensionIds,
            IReadOnlyCollection<CoverageRelationLookup> relations,
            List<string> errors)
        {
            var selectedSections = sectionIds.ToHashSet();
            var selectedExtensions = extensionIds.ToHashSet();
            var selectedParents = new[] { mainCoverId }.Concat(sectionIds).ToHashSet();

            foreach (var relation in relations.Where(relation => relation.IsMandatory && selectedParents.Contains(relation.ParentId)))
            {
                if (IsRelation(relation.RelationType, CoverageConstants.HasSectionRelation) &&
                    !selectedSections.Contains(relation.ChildId))
                {
                    errors.Add($"Mandatory section {relation.ChildId} must be selected.");
                }

                if (IsRelation(relation.RelationType, CoverageConstants.HasExtensionRelation) &&
                    !selectedExtensions.Contains(relation.ChildId))
                {
                    errors.Add($"Mandatory extension {relation.ChildId} must be selected.");
                }
            }
        }

        private static IReadOnlyList<int> NormalizeIds(IEnumerable<int>? ids)
        {
            return (ids ?? [])
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
        }

        private static bool IsLevel(string? actualLevel, string expectedLevel)
        {
            return string.Equals(actualLevel?.Trim(), expectedLevel, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRelation(string? actualRelation, string expectedRelation)
        {
            return string.Equals(actualRelation?.Trim(), expectedRelation, StringComparison.OrdinalIgnoreCase);
        }

        private static ServiceResult<CoverageSelectionPlan> ValidationFailure(string error)
        {
            return ValidationFailure([error]);
        }

        private static ServiceResult<CoverageSelectionPlan> ValidationFailure(IEnumerable<string> errors)
        {
            return ServiceResult<CoverageSelectionPlan>.Failure(
                "Validation failed.",
                errors,
                StatusCodes.Status400BadRequest);
        }
    }
}
