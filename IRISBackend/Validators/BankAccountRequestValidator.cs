using FluentValidation;
using IRISBackend.DTOs;
namespace IRISBackend.Validators;

public sealed class BankAccountRequestValidator : AbstractValidator<BankAccountRequestDto>
{
    public BankAccountRequestValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.BankCity).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BankCountry).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CurrencyId).GreaterThan(0);
        RuleFor(x => x.AccountLabel).MaximumLength(255);
        RuleFor(x => x.AgencyCode).MaximumLength(100);
        RuleFor(x => x.CorrespondentBankName).NotEmpty().MaximumLength(255).When(x => x.HasCorrespondentBank);
    }
}
