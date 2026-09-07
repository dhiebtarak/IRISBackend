using System.ComponentModel;

namespace IRISBackend.DTOs
{
    public sealed class CounterpartyLookupQueryDto
    {
        public string? Search { get; set; }

        [DefaultValue(1)]
        public int Page { get; set; } = 1;

        [DefaultValue(20)]
        public int PageSize { get; set; } = 20;
    }
}
