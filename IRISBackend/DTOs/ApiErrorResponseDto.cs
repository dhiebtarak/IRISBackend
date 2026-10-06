namespace IRISBackend.DTOs
{
    public class ApiErrorResponseDto
    {
        public string? Code { get; set; }
        public string Message { get; set; } = "An error occurred.";

        public IEnumerable<string> Errors { get; set; } = [];
    }
}
