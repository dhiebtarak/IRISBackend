namespace IRISBackend.DTOs
{
    public sealed class CoverageSelectionRequestDto
    {
        public int MainCoverId { get; set; }

        public ICollection<int> SectionIds { get; set; } = [];

        public ICollection<int> ExtensionIds { get; set; } = [];
    }
}
