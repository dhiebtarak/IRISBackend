using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators
{
    public class CounterpartyQueryDtoValidator : AbstractValidator<CounterpartyQueryDto>
    {
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
        }
    }
}
