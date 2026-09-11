namespace IRISBackend.DTOs
{
    public sealed class ContractAllowedActionsDto
    {
        public bool CanModifyContract { get; set; }

        public bool CanAddReinsurer { get; set; }

        public bool CanStartBinding { get; set; }

        public bool CanCancelContract { get; set; }

        public bool CanEditPlacement { get; set; }
    }
}
