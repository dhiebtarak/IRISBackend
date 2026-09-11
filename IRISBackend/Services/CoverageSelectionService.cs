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

            var primaryMainCoverId = request.PrimaryMainCoverId > 0
                ? request.PrimaryMainCoverId
                : request.MainCoverId;
            var mainCoverIds = NormalizeIds(request.CoverageIds);
            if (mainCoverIds.Count == 0 && request.PrimaryMainCoverId <= 0 && request.MainCoverId > 0)
            {
                mainCoverIds = [primaryMainCoverId];
            }

            var sectionIds = NormalizeIds(request.SectionIds);
            var extensionIds = NormalizeIds(request.ExtensionIds);
            var allRequestedIds = mainCoverIds.Concat(sectionIds).Concat(extensionIds).Where(id => id > 0).Distinct().ToArray();
            var errors = new List<string>();

            if (primaryMainCoverId <= 0)
            {
                errors.Add("coverageSelection.primaryMainCoverId is required.");
            }

            if (mainCoverIds.Count == 0)
            {
                errors.Add("coverageSelection.coverageIds must contain at least one coverage.");
            }
            else if (!mainCoverIds.Contains(primaryMainCoverId))
            {
                errors.Add("primaryMainCoverId must be included in coverageIds.");
            }

            if (HasDuplicates(request.CoverageIds))
            {
                errors.Add("coverageSelection.coverageIds must not contain duplicates.");
            }

            var items = await _coverageRepository.GetCoverageItemsByIdsAsync(allRequestedIds, cancellationToken);
            var itemsById = items.ToDictionary(item => item.Id);
            var primaryMainCover = itemsById.GetValueOrDefault(primaryMainCoverId);

            var mainCoverCode = primaryMainCover?.Code?.Trim().ToUpperInvariant() ?? string.Empty;
            if (primaryMainCover is not null &&
                (mainCoverCode.Length == 0 ||
                 mainCoverCode.Length > 50 ||
                 mainCoverCode.Any(character => !char.IsLetterOrDigit(character))))
            {
                errors.Add("The selected main cover has an invalid coverage code.");
            }

            foreach (var mainCoverId in mainCoverIds)
            {
                if (!itemsById.TryGetValue(mainCoverId, out var mainCover) ||
                    !mainCover.IsActive ||
                    !IsLevel(mainCover.Level, CoverageConstants.MainCoverLevel))
                {
                    errors.Add($"Selected coverage {mainCoverId} does not exist, is inactive, or is not a MAIN_COVER.");
                }
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

            var relationParentIds = mainCoverIds.Concat(sectionIds).Where(id => id > 0).Distinct().ToArray();
            var relations = await _coverageRepository.GetActiveRelationsForParentsAsync(relationParentIds, cancellationToken);

            ValidateSections(mainCoverIds, sectionIds, relations, errors);
            ValidateExtensions(mainCoverIds, sectionIds, extensionIds, relations, errors);
            ValidateMandatoryChildren(mainCoverIds, sectionIds, extensionIds, relations, errors);

            if (errors.Count > 0)
            {
                return ValidationFailure(errors);
            }

            var slipTemplateIds = await _coverageRepository.GetActiveCurrentSlipIdsAsync(allRequestedIds, cancellationToken);
            return ServiceResult<CoverageSelectionPlan>.Success(new CoverageSelectionPlan
            {
                PrimaryMainCoverId = primaryMainCoverId,
                MainCoverId = primaryMainCoverId,
                MainCoverIds = mainCoverIds,
                MainCoverCode = mainCoverCode,
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
            await AddSelectionsByLevelAsync(contractId, GetMainCoverIds(plan), plan, userId, now, cancellationToken, isMainCover: true);
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
            var currentSections = currentSelections.Where(selection => IsLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.SectionLevel)).ToList();
            var currentExtensions = currentSelections.Where(selection => IsLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.ExtensionLevel)).ToList();
            var currentMainCovers = currentSelections.Where(selection => IsLevel(selection.IdCoverageItemNavigation.CoverageLevel, CoverageConstants.MainCoverLevel)).ToList();

            _coverageRepository.RemoveSelections(currentExtensions);
            _coverageRepository.RemoveSelections(currentSections);
            await _coverageRepository.SaveChangesAsync(cancellationToken);

            _coverageRepository.RemoveSelections(currentMainCovers);
            await _coverageRepository.SaveChangesAsync(cancellationToken);

            await AddSelectionsByLevelAsync(contractId, GetMainCoverIds(plan), plan, userId, now, cancellationToken, isMainCover: true);
            await AddSelectionsByLevelAsync(contractId, plan.SectionIds, plan, userId, now, cancellationToken);
            await AddSelectionsByLevelAsync(contractId, plan.ExtensionIds, plan, userId, now, cancellationToken);
        }

        private async Task AddSelectionsByLevelAsync(
            int contractId,
            IReadOnlyCollection<int> coverageItemIds,
            CoverageSelectionPlan plan,
            string userId,
            DateTime now,
            CancellationToken cancellationToken,
            bool isMainCover = false)
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
                IsPrimary = isMainCover && coverageItemId == (plan.PrimaryMainCoverId > 0 ? plan.PrimaryMainCoverId : plan.MainCoverId),
                CreatedAt = now,
                CreatedBy = userId
            });

            await _coverageRepository.AddSelectionsAsync(selections, cancellationToken);
            await _coverageRepository.SaveChangesAsync(cancellationToken);
        }

        private static void ValidateSections(
            IReadOnlyCollection<int> mainCoverIds,
            IReadOnlyCollection<int> sectionIds,
            IReadOnlyCollection<CoverageRelationLookup> relations,
            List<string> errors)
        {
            var validSectionIds = relations
                .Where(relation =>
                    mainCoverIds.Contains(relation.ParentId) &&
                    IsRelation(relation.RelationType, CoverageConstants.HasSectionRelation))
                .Select(relation => relation.ChildId)
                .ToHashSet();

            foreach (var sectionId in sectionIds.Where(sectionId => !validSectionIds.Contains(sectionId)))
            {
                errors.Add($"Selected section {sectionId} is not related to the selected main cover.");
            }
        }

        private static void ValidateExtensions(
            IReadOnlyCollection<int> mainCoverIds,
            IReadOnlyCollection<int> sectionIds,
            IReadOnlyCollection<int> extensionIds,
            IReadOnlyCollection<CoverageRelationLookup> relations,
            List<string> errors)
        {
            var allowedParentIds = mainCoverIds.Concat(sectionIds).ToHashSet();
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
            IReadOnlyCollection<int> mainCoverIds,
            IReadOnlyCollection<int> sectionIds,
            IReadOnlyCollection<int> extensionIds,
            IReadOnlyCollection<CoverageRelationLookup> relations,
            List<string> errors)
        {
            var selectedSections = sectionIds.ToHashSet();
            var selectedExtensions = extensionIds.ToHashSet();
            var selectedParents = mainCoverIds.Concat(sectionIds).ToHashSet();

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

        private static IReadOnlyList<int> GetMainCoverIds(CoverageSelectionPlan plan)
        {
            return plan.MainCoverIds.Count > 0
                ? plan.MainCoverIds
                : plan.MainCoverId > 0 ? [plan.MainCoverId] : [];
        }

        private static bool HasDuplicates(IEnumerable<int>? ids)
        {
            var values = (ids ?? []).Where(id => id > 0).ToArray();
            return values.Length != values.Distinct().Count();
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
