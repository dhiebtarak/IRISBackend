namespace IRISBackend.DTOs
{
    public class CounterpartyAttachmentDto
    {
        public int IdDocument { get; set; }

        public string DocumentType { get; set; } = string.Empty;

        public string? OriginalFileName { get; set; }

        public string? MimeType { get; set; }

        public long? FileSize { get; set; }

        public DateTime UploadedAt { get; set; }

        public string? DownloadUrl { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
