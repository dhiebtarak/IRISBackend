namespace IRISBackend.DTOs;

public sealed class DocumentAttachmentResponseDto
{
    public int IdDocument { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string? CreatedByName { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;
}
