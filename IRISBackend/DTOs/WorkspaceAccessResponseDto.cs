namespace IRISBackend.DTOs
{
    public class WorkspaceAccessResponseDto
    {
        public string WorkspaceKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public IEnumerable<string> Roles { get; set; } = [];

        public IEnumerable<string> Permissions { get; set; } = [];
    }
}
