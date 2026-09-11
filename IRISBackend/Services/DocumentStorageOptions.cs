namespace IRISBackend.Services;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

    public string RootPath { get; set; } = Path.Combine("App_Data", "Documents");

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    public string[] AllowedExtensions { get; set; } =
    [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".txt", ".csv"];
}
