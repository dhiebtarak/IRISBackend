namespace IRISBackend.DTOs
{
    public sealed class ContractListQueryDto
    {
        public string? Search { get; set; }

        public string? ContractType { get; set; }

        public string? Status { get; set; }

        public int? CounterpartyId { get; set; }

        public int? CurrencyId { get; set; }

        public DateOnly? InceptionFrom { get; set; }

        public DateOnly? InceptionTo { get; set; }

        public DateOnly? ExpiryFrom { get; set; }

        public DateOnly? ExpiryTo { get; set; }

        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string SortBy { get; set; } = "updatedAt";

        public string SortDirection { get; set; } = "desc";
    }
}
