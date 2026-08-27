namespace IRISBackend.DTOs
{
    public sealed class ContractListItemDto
    {
        public int IdContract { get; set; }

        public string? Umr { get; set; }

        public string Name { get; set; } = string.Empty;

        public int CedentId { get; set; }

        public string CedentName { get; set; } = string.Empty;

        public string? ContractType { get; set; }

        public string TypeOfBusiness { get; set; } = string.Empty;

        public int CurrencyId { get; set; }

        public string CurrencyCode { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateOnly InceptionDate { get; set; }

        public DateOnly ExpiryDate { get; set; }

        public decimal? GrossPremium { get; set; }

        public bool ActionRequired { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
