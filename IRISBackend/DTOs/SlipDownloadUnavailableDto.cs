namespace IRISBackend.DTOs
{
    public sealed class SlipDownloadUnavailableDto
    {
        public bool Available { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
