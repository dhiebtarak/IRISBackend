namespace IRISBackend.DTOs
{
    public sealed class ContractCoverageSelectionResponseDto
    {
        public int PrimaryMainCoverId { get; set; }

        public IReadOnlyList<int> CoverageIds { get; set; } = [];

        public IReadOnlyList<ContractMainCoverSelectionItemDto> Coverages { get; set; } = [];

        // Compatibility fields retained for existing overview consumers.
        public int MainCoverId { get; set; }

        public IReadOnlyList<int> SectionIds { get; set; } = [];

        public IReadOnlyList<int> ExtensionIds { get; set; } = [];

        public ContractCoverageSelectionItemDto? MainCover { get; set; }

        public IReadOnlyList<ContractCoverageSelectionItemDto> Sections { get; set; } = [];

        public IReadOnlyList<ContractCoverageSelectionItemDto> Extensions { get; set; } = [];
    }

    public sealed class ContractMainCoverSelectionItemDto
    {
        public int IdMainCover { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public bool HasSlip { get; set; }

        public int? SlipTemplateId { get; set; }
    }

    public sealed class ContractCoverageSelectionItemDto
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool HasSlip { get; set; }

        public int? SlipTemplateId { get; set; }
    }
}
