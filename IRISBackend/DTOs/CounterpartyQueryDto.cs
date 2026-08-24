namespace IRISBackend.DTOs
{
    public class CounterpartyQueryDto
    {
        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 25;

        public string? Search { get; set; }

        public string? Type { get; set; }

        public string? Status { get; set; }

        public bool? IsActive { get; set; }
    }
}
