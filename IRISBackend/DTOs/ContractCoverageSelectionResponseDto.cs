namespace IRISBackend.DTOs
{
    public sealed class ContractCoverageSelectionResponseDto
    {
        public int MainCoverId { get; set; }

        public IReadOnlyList<int> SectionIds { get; set; } = [];

        public IReadOnlyList<int> ExtensionIds { get; set; } = [];

        public ContractCoverageSelectionItemDto? MainCover { get; set; }

        public IReadOnlyList<ContractCoverageSelectionItemDto> Sections { get; set; } = [];

        public IReadOnlyList<ContractCoverageSelectionItemDto> Extensions { get; set; } = [];
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
