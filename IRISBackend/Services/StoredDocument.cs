namespace IRISBackend.Services;

public sealed class StoredDocument
{
    public string OriginalFileName { get; init; } = string.Empty;

    public string StoredFileName { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string FileExtension { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public long FileSize { get; init; }
}
