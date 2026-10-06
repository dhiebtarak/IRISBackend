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
                .GreaterThan(0m)
                .When(panel => panel.WrittenSharePct.HasValue)
                .WithMessage("writtenSharePct must be greater than 0 and no more than 100.");

            RuleFor(panel => panel.WrittenSharePct)
                .LessThanOrEqualTo(100m)
                .When(panel => panel.WrittenSharePct.HasValue)
                .WithMessage("writtenSharePct must be greater than 0 and no more than 100.");

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

            RuleFor(panel => panel.SumInsured)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.SumInsured.HasValue)
                .WithMessage("sumInsured must be greater than or equal to 0.");

            RuleFor(panel => panel.Deductible)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.Deductible.HasValue)
                .WithMessage("deductible must be greater than or equal to 0.");

            RuleFor(panel => panel.DeductibleValue)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.DeductibleValue.HasValue)
                .WithMessage("deductibleValue must be greater than or equal to 0.");

            RuleFor(panel => panel.DeductibleType)
                .Must(value => string.IsNullOrWhiteSpace(value) ||
                    value.Trim().Equals(ContractPanelFinancialCalculator.AmountType, StringComparison.OrdinalIgnoreCase) ||
                    value.Trim().Equals(ContractPanelFinancialCalculator.PercentageType, StringComparison.OrdinalIgnoreCase))
                .WithMessage("deductibleType must be Amount or Percentage.");

            RuleFor(panel => panel.PremiumRatePct)
                .GreaterThanOrEqualTo(0m)
                .LessThanOrEqualTo(100m)
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

            RuleFor(panel => panel.BrokerageIrisPct)
                .InclusiveBetween(0m, 100m)
                .When(panel => panel.BrokerageIrisPct.HasValue)
                .WithMessage("brokerageIrisPct must be between 0 and 100.");

            RuleFor(panel => panel.CedentCommissionPct)
                .InclusiveBetween(0m, 100m)
                .When(panel => panel.CedentCommissionPct.HasValue)
                .WithMessage("cedentCommissionPct must be between 0 and 100.");

            RuleFor(panel => panel.LimitOfLiabilityValue)
                .GreaterThanOrEqualTo(0m)
                .When(panel => panel.LimitOfLiabilityValue.HasValue)
                .WithMessage("limitOfLiabilityValue must be greater than or equal to 0.");

            RuleFor(panel => panel.LimitOfLiabilityType)
                .Must(value => string.IsNullOrWhiteSpace(value) ||
                    value.Trim().Equals(ContractPanelFinancialCalculator.AmountType, StringComparison.OrdinalIgnoreCase) ||
                    value.Trim().Equals(ContractPanelFinancialCalculator.PercentageType, StringComparison.OrdinalIgnoreCase))
                .WithMessage("limitOfLiabilityType must be Amount or Percentage.");

            RuleFor(panel => panel)
                .Must(panel => !string.Equals(panel.LimitOfLiabilityType?.Trim(), ContractPanelFinancialCalculator.PercentageType, StringComparison.OrdinalIgnoreCase) ||
                    !panel.LimitOfLiabilityValue.HasValue || panel.LimitOfLiabilityValue.Value <= 100m)
                .WithMessage("limitOfLiabilityValue must be between 0 and 100 for Percentage mode.");

            RuleFor(panel => panel.LimitOfLiabilityValue)
                .NotNull()
                .When(panel => !string.IsNullOrWhiteSpace(panel.LimitOfLiabilityType))
                .WithMessage("limitOfLiabilityValue is required when limitOfLiabilityType is provided.");

            RuleFor(panel => panel.DeductibleValue)
                .NotNull()
                .When(panel => !string.IsNullOrWhiteSpace(panel.DeductibleType))
                .WithMessage("deductibleValue is required when deductibleType is provided.");

            RuleFor(panel => panel)
                .Must(panel => !string.Equals(panel.DeductibleType?.Trim(), ContractPanelFinancialCalculator.PercentageType, StringComparison.OrdinalIgnoreCase) ||
                    !panel.DeductibleValue.HasValue || panel.DeductibleValue.Value <= 100m)
                .WithMessage("deductibleValue must be between 0 and 100 for Percentage mode.");

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
