namespace IRISBackend.DTOs
{
    public sealed class CedentLookupItemDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Country { get; set; }

        public string? City { get; set; }
    }
}
