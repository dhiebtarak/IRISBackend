using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;

namespace IRISBackend.Validators
{
    public class CounterpartyUpdateDtoValidator : AbstractValidator<CounterpartyUpdateDto>
    {
        public CounterpartyUpdateDtoValidator()
        {
            var attachmentValidator = new AttachmentUploadDtoValidator();

            RuleFor(counterparty => counterparty.LegacyClientId)
                .GreaterThan(0)
                .When(counterparty => counterparty.LegacyClientId.HasValue);

            RuleFor(counterparty => counterparty.LegacyCompanyCode)
                .MaximumLength(64);

            RuleFor(counterparty => counterparty.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(counterparty => counterparty.Types)
                .NotNull()
                .Must(types => types is { Count: > 0 })
                .WithMessage("At least one counterparty type is required.");

            RuleForEach(counterparty => counterparty.Types)
                .NotEmpty()
                .Must(type => CounterpartyTypes.TryNormalize(type, out _))
                .WithMessage("Counterparty type must be one of: Cedent, Reinsurer, Client, Partner.");

            RuleFor(counterparty => counterparty.Types)
                .Must(types => types is null || types.Count == types.Select(type => type.Trim().ToLowerInvariant()).Distinct().Count())
                .WithMessage("Counterparty types must not contain duplicates.");

            RuleFor(counterparty => counterparty.Email)
                .MaximumLength(256)
                .EmailAddress()
                .When(counterparty => !string.IsNullOrWhiteSpace(counterparty.Email));

            RuleFor(counterparty => counterparty.PhoneNumber)
                .MaximumLength(32);

            RuleFor(counterparty => counterparty.AddressLine)
                .MaximumLength(256);

            RuleFor(counterparty => counterparty.City)
                .MaximumLength(100);

            RuleFor(counterparty => counterparty.Country)
                .MaximumLength(100);

            RuleFor(counterparty => counterparty.PostalCode)
                .MaximumLength(20);

            RuleFor(counterparty => counterparty.Website)
                .MaximumLength(256);

            RuleFor(counterparty => counterparty.Activity)
                .MaximumLength(128);

            RuleFor(counterparty => counterparty.CommercialRegister)
                .SetValidator(attachmentValidator!)
                .When(counterparty => counterparty.CommercialRegister is not null);

            RuleFor(counterparty => counterparty.CedantLegalEntity)
                .MaximumLength(200);

            RuleFor(counterparty => counterparty.Observations)
                .MaximumLength(1000);

            RuleFor(counterparty => counterparty.TaxIdAttachment)
                .SetValidator(attachmentValidator!)
                .When(counterparty => counterparty.TaxIdAttachment is not null);

            RuleFor(counterparty => counterparty.Status)
                .NotEmpty()
                .MaximumLength(64);
        }
    }
}
