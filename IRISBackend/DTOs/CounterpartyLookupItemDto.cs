namespace IRISBackend.DTOs
{
    public sealed class CounterpartyLookupItemDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public IReadOnlyCollection<string> Types { get; set; } = [];

        public string? Country { get; set; }

        public string? City { get; set; }
    }
}
