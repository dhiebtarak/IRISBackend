namespace IRISBackend.Services
{
    public static class ContractConstants
    {
        public const string FacultativeType = "Facultative";
        public const string DraftStatus = "Draft";
        public const string ReadyForPlacementStatus = "Ready for Placement";
        public const string PlacementInProgressStatus = "Placement In Progress";
        public const string ReadyForBindingStatus = "Ready for Binding";
        public const string BindingInProgressStatus = "Binding In Progress";
        public const string BoundStatus = "Bound";
        public const string CancelledStatus = "Cancelled";
        public const string ConfirmedPanelStatus = "Confirmed";
        public const string FrontOfficerDepartment = "Reassurance";
        public const string FrontOfficerPosition = "Front officer";

        public static readonly string[] WorkflowStatuses =
        [
            DraftStatus,
            ReadyForPlacementStatus,
            PlacementInProgressStatus,
            ReadyForBindingStatus,
            BindingInProgressStatus,
            BoundStatus,
            CancelledStatus
        ];

        public static readonly string[] PanelStatuses =
        [
            DraftStatus,
            ConfirmedPanelStatus
        ];
    }
}
