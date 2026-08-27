namespace IRISBackend.Services
{
    public static class ContractWorkflowStatusCategories
    {
        // Draft is the only contract workflow status currently defined by the backend.
        public static readonly IReadOnlySet<string> PendingReview =
            new HashSet<string>([ContractConstants.DraftStatus], StringComparer.OrdinalIgnoreCase);

        public static readonly IReadOnlySet<string> BoundActive =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
