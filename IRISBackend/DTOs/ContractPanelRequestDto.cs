namespace IRISBackend.DTOs
{
    public sealed class ContractPanelRequestDto
    {
        public int IdCounterparty { get; set; }

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Tsi { get; set; }

        public decimal? Deductible { get; set; }

        public decimal? PremiumRatePct { get; set; }

        public decimal? GrossPremium { get; set; }

        public decimal? BrokeragePct { get; set; }

        public decimal? OtherDeductions { get; set; }

        public string? PremiumReference { get; set; }

        public string? Status { get; set; }
    }
}
