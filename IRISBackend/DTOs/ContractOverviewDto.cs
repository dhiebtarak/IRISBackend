namespace IRISBackend.DTOs
{
    public sealed class ContractOverviewDto
    {
        public int IdContract { get; set; }

        public string? Umr { get; set; }

        public string? Name { get; set; }

        public string? ContractType { get; set; }

        public string TypeOfBusiness { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public ContractAllowedActionsDto AllowedActions { get; set; } = new();

        public int? CounterpartyId { get; set; }

        public CedentSummaryDto? Cedent { get; set; }

        public int? CurrencyId { get; set; }

        public CurrencyReferenceDto? Currency { get; set; }

        public DateOnly CreationDate { get; set; }

        public DateOnly? ReceptionDate { get; set; }

        public DateOnly? InceptionDate { get; set; }

        public DateOnly? ExpiryDate { get; set; }

        public decimal? WrittenSharePct { get; set; }

        public decimal? SignedSharePct { get; set; }

        public decimal? Tsi { get; set; }

        public decimal? SumInsured { get; set; }

        public decimal? LimitOfLiability { get; set; }

        public decimal? Deductible { get; set; }

        public decimal? PremiumRate { get; set; }

        public decimal? Fees { get; set; }

        public decimal? GrossPremium { get; set; }

        public decimal? IrisCommission { get; set; }

        public decimal? CedentCommissionPct { get; set; }

        public decimal? CedentCommission { get; set; }

        public decimal? CedentCommissionPercentage { get; set; }

        public decimal? CedentCommissionAmount { get; set; }

        public decimal? NetPremiumCedent { get; set; }

        public decimal? NetPremium { get; set; }

        public bool? IsFronting { get; set; }

        public string? FrontingReference { get; set; }

        public bool? IsHighPriority { get; set; }

        public bool? ActionRequired { get; set; }

        public ContractAuditUserDto? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public ContractAuditUserDto? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public ContractCoverageSelectionResponseDto? CoverageSelection { get; set; }

        public ContractSlipAvailabilityDto SlipAvailability { get; set; } = new();

        public IReadOnlyList<ContractPanelResponseDto> Panels { get; set; } = [];

        public decimal AvailableShare { get; set; }

        public IReadOnlyList<ContractReceiptAllocationDto> ReceiptAllocations { get; set; } = [];

        public decimal PairedAmount { get; set; }

        public decimal AlreadyInvoicedAmount { get; set; }

        public decimal AvailableAmount { get; set; }

        public bool HasAvoir { get; set; }

        public string ContractDocumentType { get; set; } = "DEBIT_NOTE";

        public string ContractDocumentDownloadUrl { get; set; } = string.Empty;
    }

    public sealed class ContractAuditUserDto
    {
        public string? Id { get; set; }

        public string? FullName { get; set; }

        public string? UserName { get; set; }

        public string? Email { get; set; }
    }

    public sealed class ContractSlipAvailabilityDto
    {
        public int SelectedCoverageItemCount { get; set; }

        public int AvailableSlipCount { get; set; }

        public int MissingSlipCount { get; set; }

        public bool HasAnySlip => AvailableSlipCount > 0;

        public bool HasAllSlips => SelectedCoverageItemCount > 0 && MissingSlipCount == 0;
    }

    public sealed class ContractReceiptAllocationDto
    {
        public int IdReceiptAllocation { get; set; }
        public int IdBankReceipt { get; set; }
        public decimal AllocatedAmount { get; set; }
        public int CurrencyId { get; set; }
    }
}
