namespace IRISBackend.DTOs
{
    public sealed class CoverageSelectionRequestDto
    {
        public int PrimaryMainCoverId { get; set; }

        public ICollection<int> CoverageIds { get; set; } = [];

        // Kept for compatibility with older clients. New clients must send primaryMainCoverId.
        public int MainCoverId { get; set; }

        public ICollection<int> SectionIds { get; set; } = [];

        public ICollection<int> ExtensionIds { get; set; } = [];
    }
}
