using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators;

public sealed class CounterpartyContactDtoValidator : AbstractValidator<CounterpartyContactDto>
{
    public CounterpartyContactDtoValidator()
    {
        RuleFor(contact => contact.Email).MaximumLength(256).EmailAddress()
            .When(contact => !string.IsNullOrWhiteSpace(contact.Email));
        RuleFor(contact => contact.PhoneNumber).MaximumLength(32);
        RuleFor(contact => contact).Must(contact =>
                !string.IsNullOrWhiteSpace(contact.Email) || !string.IsNullOrWhiteSpace(contact.PhoneNumber))
            .WithMessage("Enter an email address or phone number for this contact.");
    }
}
