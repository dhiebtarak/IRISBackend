namespace IRISBackend.DTOs;

public sealed class DocumentUploadRequestDto
{
    public string FileName { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public string ContentBase64 { get; set; } = string.Empty;
}
