using System.Text.Json.Serialization;

namespace IRISBackend.DTOs
{
    public class PagedResponseDto<T>
    {
        public IEnumerable<T> Items { get; set; } = [];

        public int PageNumber { get; set; }

        [JsonPropertyName("page")]
        public int Page => PageNumber;

        public int PageSize { get; set; }

        public int TotalCount { get; set; }

        public int TotalPages { get; set; }
    }
}
