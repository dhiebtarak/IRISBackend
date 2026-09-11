namespace IRISBackend.Services
{
    public sealed class CoverageSelectionPlan
    {
        public int PrimaryMainCoverId { get; init; }

        // Compatibility alias for existing internal callers and tests.
        public int MainCoverId { get; init; }

        public IReadOnlyList<int> MainCoverIds { get; init; } = [];

        public string MainCoverCode { get; init; } = string.Empty;

        public IReadOnlyList<int> SectionIds { get; init; } = [];

        public IReadOnlyList<int> ExtensionIds { get; init; } = [];

        public IReadOnlyDictionary<int, int> SlipTemplateIdsByCoverageItemId { get; init; } = new Dictionary<int, int>();

        public IReadOnlyList<int> AllCoverageItemIds =>
            [.. (MainCoverIds.Count > 0 ? MainCoverIds : MainCoverId > 0 ? [MainCoverId] : []), .. SectionIds, .. ExtensionIds];
    }
}
