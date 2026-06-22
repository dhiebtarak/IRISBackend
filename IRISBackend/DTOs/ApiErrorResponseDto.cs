namespace IRISBackend.DTOs
{
    public class ApiErrorResponseDto
    {
        public string Message { get; set; } = "An error occurred.";

        public IEnumerable<string> Errors { get; set; } = [];
    }
}
