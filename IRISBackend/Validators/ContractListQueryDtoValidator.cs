using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;

namespace IRISBackend.Validators
{
    public sealed class ContractListQueryDtoValidator : AbstractValidator<ContractListQueryDto>
    {
        private static readonly string[] SortFields =
        [
            "name", "umr", "cedentName", "contractType", "status", "inceptionDate",
            "expiryDate", "grossPremium", "createdAt", "updatedAt"
        ];

        public ContractListQueryDtoValidator()
        {
            RuleFor(query => query.PageNumber)
                .GreaterThanOrEqualTo(1)
                .WithMessage("pageNumber must be greater than or equal to 1.");

            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("pageSize must be between 1 and 100.");

            RuleFor(query => query.Search).MaximumLength(200);
            RuleFor(query => query.Status).MaximumLength(50);

            RuleFor(query => query.ContractType)
                .Must(value => string.IsNullOrWhiteSpace(value) ||
                    ContractTypeNames.All.Contains(ContractTypeNames.NormalizeFilter(value), StringComparer.OrdinalIgnoreCase))
                .WithMessage("contractType is invalid.");

            RuleFor(query => query.CounterpartyId)
                .GreaterThan(0)
                .When(query => query.CounterpartyId.HasValue);

            RuleFor(query => query.CurrencyId)
                .GreaterThan(0)
                .When(query => query.CurrencyId.HasValue);

            RuleFor(query => query)
                .Must(query => !query.InceptionFrom.HasValue || !query.InceptionTo.HasValue || query.InceptionFrom <= query.InceptionTo)
                .WithMessage("inceptionFrom must be on or before inceptionTo.");

            RuleFor(query => query)
                .Must(query => !query.ExpiryFrom.HasValue || !query.ExpiryTo.HasValue || query.ExpiryFrom <= query.ExpiryTo)
                .WithMessage("expiryFrom must be on or before expiryTo.");

            RuleFor(query => query.SortBy)
                .Must(value => SortFields.Contains(value, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"sortBy must be one of: {string.Join(", ", SortFields)}.");

            RuleFor(query => query.SortDirection)
                .Must(value => value.Equals("asc", StringComparison.OrdinalIgnoreCase) || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
                .WithMessage("sortDirection must be either asc or desc.");
        }
    }
}
