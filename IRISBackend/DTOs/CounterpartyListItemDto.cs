namespace IRISBackend.DTOs
{
    public class CounterpartyListItemDto
    {
        public int Id { get; set; }

        public int? LegacyClientId { get; set; }

        public string? LegacyCompanyCode { get; set; }

        public string Name { get; set; } = string.Empty;

        public IReadOnlyCollection<string> Types { get; set; } = [];

        public string? Email { get; set; }

        public string? Country { get; set; }

        public string? City { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public bool HasActiveBankAccount { get; set; }

        public int ActiveBankAccountCount { get; set; }
    }
}
