namespace IRISBackend.DTOs
{
    public sealed class ContractPanelRequestDto
    {
        public int IdCounterparty { get; set; }

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Tsi { get; set; }

        public decimal? SumInsured { get; set; }

        public decimal? Deductible { get; set; }

        public string? LimitOfLiabilityType { get; set; }

        public decimal? LimitOfLiabilityValue { get; set; }

        // Accepted for response compatibility, but always recalculated by the backend.
        public decimal? LimitOfLiabilityAmount { get; set; }

        public string? DeductibleType { get; set; }

        public decimal? DeductibleValue { get; set; }

        // Accepted for response compatibility, but always recalculated by the backend.
        public decimal? DeductibleAmount { get; set; }

        public decimal? PremiumRatePct { get; set; }

        public decimal? GrossPremium { get; set; }

        // Accepted for response compatibility, but always recalculated by the backend.
        public decimal? NetPremium { get; set; }

        public decimal? BrokeragePct { get; set; }

        public decimal? CedentCommissionPct { get; set; }

        // Accepted for response compatibility, but always recalculated by the backend.
        public decimal? CedentCommissionAmount { get; set; }

        public decimal? BrokerageIrisPct { get; set; }

        // Accepted for response compatibility, but always recalculated by the backend.
        public decimal? BrokerageIrisAmount { get; set; }

        public decimal? OtherDeductions { get; set; }

        public decimal? OtherDeductionsPercentage { get; set; }

        public string? PremiumReference { get; set; }

        public string? Status { get; set; }

    }
}
