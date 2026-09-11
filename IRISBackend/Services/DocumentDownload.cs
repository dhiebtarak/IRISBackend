namespace IRISBackend.Services;

public sealed class DocumentDownload
{
    public required Stream Content { get; init; }

    public required string FileName { get; init; }

    public required string MimeType { get; init; }
}
