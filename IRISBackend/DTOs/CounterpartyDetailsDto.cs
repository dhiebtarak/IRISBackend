namespace IRISBackend.DTOs
{
    public class CounterpartyDetailsDto
    {
        public int Id { get; set; }

        public int? LegacyClientId { get; set; }

        public string? LegacyCompanyCode { get; set; }

        public string Name { get; set; } = string.Empty;

        public IReadOnlyCollection<string> Types { get; set; } = [];

        public string? Email { get; set; }

        public string? PhoneNumber { get; set; }

        public string? AddressLine { get; set; }

        public string? City { get; set; }

        public string? Country { get; set; }

        public string? PostalCode { get; set; }

        public string? Website { get; set; }

        public string? Activity { get; set; }

        public CounterpartyAttachmentDto? CommercialRegisterAttachment { get; set; }

        public string? CedantLegalEntity { get; set; }

        public string? Observations { get; set; }

        public CounterpartyAttachmentDto? TaxIdentificationNumberAttachment { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
    }
}
