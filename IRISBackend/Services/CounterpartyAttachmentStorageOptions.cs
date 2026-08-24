namespace IRISBackend.Services
{
    public class CounterpartyAttachmentStorageOptions
    {
        public const string SectionName = "CounterpartyAttachments";
        public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

        public string RootPath { get; set; } = Path.Combine("App_Data", "Uploads");

        public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

        public string[] AllowedExtensions { get; set; } = [".pdf", ".png", ".jpg", ".jpeg"];
    }
}
