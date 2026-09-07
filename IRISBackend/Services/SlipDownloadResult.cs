namespace IRISBackend.Services
{
    public sealed class SlipDownloadResult
    {
        public bool Available { get; init; } = true;

        public byte[] Content { get; init; } = [];

        public string ContentType { get; init; } = "application/octet-stream";

        public string FileName { get; init; } = string.Empty;

        public int SlipCount { get; init; }

        public string? Message { get; init; }
    }
}
