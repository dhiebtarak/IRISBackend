namespace IRISBackend.DTOs
{
    public sealed class FacultativeContractResponseDto
    {
        public int Id { get; set; }

        public string? Umr { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ContractType { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public int CounterpartyId { get; set; }

        public CedentSummaryDto Cedent { get; set; } = new();

        public int CurrencyId { get; set; }

        public CurrencyReferenceDto Currency { get; set; } = new();

        public DateOnly CreationDate { get; set; }

        public DateOnly? ReceptionDate { get; set; }

        public DateOnly InceptionDate { get; set; }

        public DateOnly ExpiryDate { get; set; }

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Fees { get; set; }

        public bool IsHighPriority { get; set; }

        public decimal? SumInsured { get; set; }

        public decimal? LimitOfLiability { get; set; }

        public decimal? Deductible { get; set; }

        public decimal? PremiumRate { get; set; }

        public decimal? GrossPremium { get; set; }

        public decimal? CedentCommissionPct { get; set; }

        public decimal? NetPremiumCedent { get; set; }

        public string? CedentPremiumReference { get; set; }

        public bool IsFronting { get; set; }

        public string? FrontingReference { get; set; }

        public string? MemoText { get; set; }

        public bool IsCurrent { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public ContractCoverageSelectionResponseDto? CoverageSelection { get; set; }
    }
}
