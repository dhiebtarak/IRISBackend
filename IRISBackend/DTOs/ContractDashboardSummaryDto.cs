namespace IRISBackend.DTOs
{
    public sealed class ContractDashboardSummaryDto
    {
        public int TotalContracts { get; set; }

        public int PendingReviewContracts { get; set; }

        public int BoundActiveContracts { get; set; }

        public int ActionRequiredContracts { get; set; }

        public IReadOnlyList<ContractWorkflowDistributionDto> WorkflowDistribution { get; set; } = [];

        public IReadOnlyList<RecentContractDto> RecentContracts { get; set; } = [];
    }

    public sealed class ContractWorkflowDistributionDto
    {
        public string Status { get; set; } = string.Empty;

        public int Count { get; set; }

        public decimal Percentage { get; set; }
    }

    public sealed class RecentContractDto
    {
        public int IdContract { get; set; }

        public string? Umr { get; set; }

        public string Name { get; set; } = string.Empty;

        public int CedentId { get; set; }

        public string CedentName { get; set; } = string.Empty;

        public string? ContractType { get; set; }

        public string Status { get; set; } = string.Empty;

        public string CurrencyCode { get; set; } = string.Empty;

        public DateOnly InceptionDate { get; set; }

        public DateOnly ExpiryDate { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
