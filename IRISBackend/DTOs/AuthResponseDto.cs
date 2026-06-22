namespace IRISBackend.DTOs
{
    public class AuthResponseDto
    {
        public string Id { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public IEnumerable<string> Roles { get; set; } = [];

        public string Token { get; set; } = string.Empty;

        public UserProfileResponseDto User { get; set; } = new();
    }
}
