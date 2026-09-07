using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators
{
    public sealed class CounterpartyLookupQueryDtoValidator : AbstractValidator<CounterpartyLookupQueryDto>
    {
        public CounterpartyLookupQueryDtoValidator()
        {
            RuleFor(query => query.Page)
                .GreaterThanOrEqualTo(1)
                .WithMessage("page must be greater than or equal to 1.");

            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, 50)
                .WithMessage("pageSize must be between 1 and 50.");

            RuleFor(query => query.Search)
                .MaximumLength(200);
        }
    }
}
