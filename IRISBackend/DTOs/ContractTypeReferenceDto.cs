namespace IRISBackend.DTOs
{
    public sealed class ContractTypeReferenceDto
    {
        public string Code { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsImplemented { get; set; }
    }
}
