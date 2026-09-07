namespace IRISBackend.DTOs
{
    public sealed class CoverageCatalogItemDto
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public string? RelationType { get; set; }

        public bool IsMandatory { get; set; }

        public bool HasSlip { get; set; }
    }
}
