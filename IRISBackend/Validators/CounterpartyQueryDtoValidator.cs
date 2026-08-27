using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators
{
    public class CounterpartyQueryDtoValidator : AbstractValidator<CounterpartyQueryDto>
    {
        private static readonly string[] SortFields =
        [
            "name", "type", "email", "status", "country", "city",
            "legacyCompanyCode", "createdAt", "updatedAt"
        ];

        public CounterpartyQueryDtoValidator()
        {
            RuleFor(query => query.PageNumber)
                .GreaterThanOrEqualTo(1)
                .WithMessage("pageNumber must be greater than or equal to 1.");

            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("pageSize must be between 1 and 100.");

            RuleFor(query => query.Search)
                .MaximumLength(200);

            RuleFor(query => query.Type)
                .MaximumLength(64);

            RuleFor(query => query.Status)
                .MaximumLength(64);

            RuleFor(query => query.Country)
                .MaximumLength(100);

            RuleFor(query => query.City)
                .MaximumLength(100);

            RuleFor(query => query.SortBy)
                .Must(value => SortFields.Contains(value, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"sortBy must be one of: {string.Join(", ", SortFields)}.");

            RuleFor(query => query.SortDirection)
                .Must(value => value.Equals("asc", StringComparison.OrdinalIgnoreCase) || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
                .WithMessage("sortDirection must be either asc or desc.");
        }
    }
}
