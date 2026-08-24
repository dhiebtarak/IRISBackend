namespace IRISBackend.DTOs
{
    public class AttachmentUploadDto
    {
        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public string Base64Content { get; set; } = string.Empty;
    }
}
