namespace IRISBackend.Services
{
    public static class ContractConstants
    {
        public const string FacultativeType = "Facultative";
        public const string DraftStatus = "Draft";
        public const string ReadyForPlacementStatus = "ReadyForPlacement";
        public const string PlacementInProgressStatus = "PlacementInProgress";
        public const string BoundStatus = "Bound";
        public const string ExpiredStatus = "Expired";
        public const string CancelledStatus = "Cancelled";
        public const string ConfirmedPanelStatus = "Confirmed";
        public const string NotTakenUpPanelStatus = "NotTakenUp";
        public const string CancelledPanelStatus = "Cancelled";
        public const string FrontOfficerDepartment = "Reassurance";
        public const string FrontOfficerPosition = "Front officer";

        public static readonly string[] WorkflowStatuses =
        [
            DraftStatus,
            ReadyForPlacementStatus,
            PlacementInProgressStatus,
            BoundStatus,
            ExpiredStatus,
            CancelledStatus
        ];

        public static readonly string[] PanelStatuses =
        [
            DraftStatus,
            ConfirmedPanelStatus,
            NotTakenUpPanelStatus
        ];

        public static string NormalizeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return DraftStatus;
            }

            var key = new string(status.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return key switch
            {
                "draft" => DraftStatus,
                "readyforplacement" => ReadyForPlacementStatus,
                "placementinprogress" => PlacementInProgressStatus,
                "bound" => BoundStatus,
                "expired" => ExpiredStatus,
                "cancelled" or "canceled" => CancelledStatus,
                _ => status.Trim()
            };
        }

        public static string DisplayLabel(string? status)
        {
            return NormalizeStatus(status) switch
            {
                DraftStatus => "Draft",
                ReadyForPlacementStatus => "Ready for placement",
                PlacementInProgressStatus => "Placement in progress",
                BoundStatus => "Bound",
                ExpiredStatus => "Expired",
                CancelledStatus => "Cancelled",
                _ => status?.Trim() ?? string.Empty
            };
        }

        public static IReadOnlyList<string> StatusAliases(string status)
        {
            return NormalizeStatus(status) switch
            {
                ReadyForPlacementStatus => [ReadyForPlacementStatus, "Ready for Placement"],
                PlacementInProgressStatus => [PlacementInProgressStatus, "Placement In Progress"],
                CancelledStatus => [CancelledStatus, "Canceled"],
                _ => [NormalizeStatus(status)]
            };
        }
    }
}
