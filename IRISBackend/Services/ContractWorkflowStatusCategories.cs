namespace IRISBackend.Services
{
    public static class ContractWorkflowStatusCategories
    {
        public static readonly IReadOnlySet<string> PendingReview =
            new HashSet<string>(
                [
                    ContractConstants.DraftStatus,
                    ContractConstants.ReadyForPlacementStatus,
                    ContractConstants.PlacementInProgressStatus
                ],
                StringComparer.OrdinalIgnoreCase);

        public static readonly IReadOnlySet<string> BoundActive =
            new HashSet<string>([ContractConstants.BoundStatus], StringComparer.OrdinalIgnoreCase);
    }
}
