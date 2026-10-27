namespace IRISBackend.DTOs
{
    using System.Text.Json.Serialization;

    public sealed class ContractPanelResponseDto
    {
        public int IdPanel { get; set; }

        public int IdContract { get; set; }

        public decimal? ContractGrossPremium { get; set; }

        public decimal? ContractSignedSharePercentage { get; set; }

        public decimal? ContractNetPremium { get; set; }

        public decimal? ContractCedentCommissionPercentage { get; set; }

        public decimal? ContractCedentCommissionAmount { get; set; }

        [JsonIgnore]
        public int? CurrencyDecimalPlaces { get; set; }

        public int IdCounterparty { get; set; }

        public string ReinsurerName { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Tsi { get; set; }

        public decimal? SumInsured { get; set; }

        public decimal? Deductible { get; set; }

        public string? LimitOfLiabilityType { get; set; }

        public decimal? LimitOfLiabilityValue { get; set; }

        public decimal? LimitOfLiabilityAmount { get; set; }

        public string? DeductibleType { get; set; }

        public decimal? DeductibleValue { get; set; }

        public decimal? DeductibleAmount { get; set; }

        public decimal? PremiumRatePct { get; set; }

        public decimal? GrossPremium { get; set; }

        public decimal? BrokeragePct { get; set; }

        public decimal? CedentCommissionPct { get; set; }

        public decimal? CedentCommissionAmount { get; set; }

        public decimal? BrokerageIrisPct { get; set; }

        public decimal? BrokerageIrisAmount { get; set; }

        public decimal? OtherDeductions { get; set; }

        public decimal? OtherDeductionsPercentage { get; set; }

        public decimal? OtherDeductionsAmount { get; set; }

        public decimal? NetPremium { get; set; }

        public string? PremiumReference { get; set; }

        public string Status { get; set; } = string.Empty;

        public decimal? AvailableShare { get; set; }
    }
}
