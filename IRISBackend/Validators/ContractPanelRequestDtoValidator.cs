using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;

namespace IRISBackend.Validators
{
    public sealed class ContractPanelRequestDtoValidator : AbstractValidator<ContractPanelRequestDto>
    {
        public ContractPanelRequestDtoValidator()
        {
            RuleFor(panel => panel.IdCounterparty)
                .GreaterThan(0)
                .WithMessage("idCounterparty must be the selected reinsurer id.");

            RuleFor(panel => panel.WrittenSharePct)
                .InclusiveBetween(0m, 100m)
                .When(panel => panel.WrittenSharePct.HasValue)
                .WithMessage("writtenSharePct must be between 0 and 100.");

            RuleFor(panel => panel.SignedSharePct)
                .NotNull()
                .WithMessage("signedSharePct is required.")
                .InclusiveBetween(0m, 100m)
                .When(panel => panel.SignedSharePct.HasValue, ApplyConditionTo.CurrentValidator)
                .WithMessage("signedSharePct must be between 0 and 100.");

            RuleFor(panel => panel)
                .Must(panel =>
                    !panel.WrittenSharePct.HasValue ||
                    !panel.SignedSharePct.HasValue ||
                    panel.SignedSharePct.Value <= panel.WrittenSharePct.Value)
                .WithMessage("signedSharePct cannot exceed writtenSharePct.");

            RuleFor(panel => panel.Tsi)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.Tsi.HasValue)
                .WithMessage("tsi must be greater than or equal to 0.");

            RuleFor(panel => panel.Deductible)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.Deductible.HasValue)
                .WithMessage("deductible must be greater than or equal to 0.");

            RuleFor(panel => panel.PremiumRatePct)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.PremiumRatePct.HasValue)
                .WithMessage("premiumRatePct must be greater than or equal to 0.");

            RuleFor(panel => panel.GrossPremium)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.GrossPremium.HasValue)
                .WithMessage("grossPremium must be greater than or equal to 0.");

            RuleFor(panel => panel.BrokeragePct)
                .InclusiveBetween(0m, 100m)
                .When(panel => panel.BrokeragePct.HasValue)
                .WithMessage("brokeragePct must be between 0 and 100.");

            RuleFor(panel => panel.OtherDeductions)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.OtherDeductions.HasValue)
                .WithMessage("otherDeductions must be greater than or equal to 0.");

            RuleFor(panel => panel.PremiumReference)
                .MaximumLength(100);

            RuleFor(panel => panel.Status)
                .Must(value => string.IsNullOrWhiteSpace(value) ||
                    ContractConstants.PanelStatuses.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage("status is invalid.");
        }
    }
}
