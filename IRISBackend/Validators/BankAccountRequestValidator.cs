using FluentValidation;
using IRISBackend.DTOs;
using System.Text.RegularExpressions;
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
        RuleFor(x => x.Rib).MaximumLength(100);
        RuleFor(x => x.Iban).NotEmpty().MaximumLength(100).Must(v => Match(v, "^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$")).WithMessage("Enter a valid IBAN of 15-34 characters, including country and check digits.");
        RuleFor(x => x.SwiftBic).NotEmpty().MaximumLength(100).Must(v => Match(v, "^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$")).WithMessage("Enter a valid 8- or 11-character SWIFT/BIC.");
        RuleFor(x => x.CorrespondentBankName).NotEmpty().MaximumLength(255).When(x => x.HasCorrespondentBank);
        RuleFor(x => x.CorrespondentSwiftBic).NotEmpty().MaximumLength(100).Must(v => Match(v, "^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$")).When(x => x.HasCorrespondentBank).WithMessage("Enter a valid correspondent SWIFT/BIC.");
    }
    private static bool Match(string? value, string pattern) => value != null && Regex.IsMatch(string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant(), pattern);
}
