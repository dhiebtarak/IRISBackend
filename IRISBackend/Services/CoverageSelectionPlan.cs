namespace IRISBackend.Services
{
    public sealed class CoverageSelectionPlan
    {
        public int MainCoverId { get; init; }

        public IReadOnlyList<int> SectionIds { get; init; } = [];

        public IReadOnlyList<int> ExtensionIds { get; init; } = [];

        public IReadOnlyDictionary<int, int> SlipTemplateIdsByCoverageItemId { get; init; } = new Dictionary<int, int>();

        public IReadOnlyList<int> AllCoverageItemIds => [MainCoverId, .. SectionIds, .. ExtensionIds];
    }
}
