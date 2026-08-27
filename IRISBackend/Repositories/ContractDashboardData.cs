using IRISBackend.DTOs;

namespace IRISBackend.Repositories
{
    public sealed class ContractDashboardData
    {
        public int TotalContracts { get; init; }

        public int ActionRequiredContracts { get; init; }

        public IReadOnlyList<ContractStatusCount> StatusCounts { get; init; } = [];

        public IReadOnlyList<RecentContractDto> RecentContracts { get; init; } = [];
    }

    public sealed class ContractStatusCount
    {
        public string Status { get; init; } = string.Empty;

        public int Count { get; init; }
    }
}
