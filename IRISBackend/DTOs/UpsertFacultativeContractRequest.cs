namespace IRISBackend.DTOs
{
    public sealed class UpsertFacultativeContractRequest
    {
        public string? Name { get; set; }

        public int? CounterpartyId { get; set; }

        public int? CurrencyId { get; set; }

        public DateOnly? ReceptionDate { get; set; }

        public DateOnly? InceptionDate { get; set; }

        public DateOnly? ExpiryDate { get; set; }

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Fees { get; set; }

        public bool? IsHighPriority { get; set; }

        public decimal? SumInsured { get; set; }

        public decimal? LimitOfLiability { get; set; }

        public decimal? Deductible { get; set; }

        public decimal? PremiumRate { get; set; }

        public decimal? GrossPremium { get; set; }

        public decimal? CedentCommissionPct { get; set; }

        public string? CedentPremiumReference { get; set; }

        public bool? IsFronting { get; set; }

        public string? FrontingReference { get; set; }

        public string? MemoText { get; set; }

        public CoverageSelectionRequestDto? CoverageSelection { get; set; }
    }
}
