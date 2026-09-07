namespace IRISBackend.DTOs
{
    public sealed class ContractPanelResponseDto
    {
        public int IdPanel { get; set; }

        public int IdContract { get; set; }

        public int IdCounterparty { get; set; }

        public string ReinsurerName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Tsi { get; set; }

        public decimal? Deductible { get; set; }

        public decimal? PremiumRatePct { get; set; }

        public decimal? GrossPremium { get; set; }

        public decimal? BrokeragePct { get; set; }

        public decimal? OtherDeductions { get; set; }

        public decimal? NetPremium { get; set; }

        public string? PremiumReference { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
