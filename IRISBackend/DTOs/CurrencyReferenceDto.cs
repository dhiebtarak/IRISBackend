namespace IRISBackend.DTOs
{
    public sealed class CurrencyReferenceDto
    {
        public int? Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string? Symbol { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? DecimalPlaces { get; set; }

        public bool IsDefault { get; set; }
    }
}
