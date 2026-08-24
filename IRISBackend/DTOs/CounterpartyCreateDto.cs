namespace IRISBackend.DTOs
{
    public class CounterpartyCreateDto
    {
        public int? LegacyClientId { get; set; }

        public string? LegacyCompanyCode { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string? PhoneNumber { get; set; }

        public string? AddressLine { get; set; }

        public string? City { get; set; }

        public string? Country { get; set; }

        public string? PostalCode { get; set; }

        public string? Website { get; set; }

        public string? Activity { get; set; }

        public AttachmentUploadDto? CommercialRegister { get; set; }

        public string? CedantLegalEntity { get; set; }

        public string? Observations { get; set; }

        public AttachmentUploadDto? TaxIdAttachment { get; set; }

        public string Status { get; set; } = "Active";
    }
}
