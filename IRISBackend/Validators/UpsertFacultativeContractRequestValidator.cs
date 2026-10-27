using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;

namespace IRISBackend.Validators;

public sealed class UpsertFacultativeContractRequestValidator : AbstractValidator<UpsertFacultativeContractRequest>
{
    public UpsertFacultativeContractRequestValidator()
    {
        RuleFor(contract => contract.Name).MaximumLength(255);
        RuleFor(contract => contract.CounterpartyId).GreaterThan(0).When(contract => contract.CounterpartyId.HasValue);
        RuleFor(contract => contract.CurrencyId).GreaterThan(0).When(contract => contract.CurrencyId.HasValue);

        RuleFor(contract => contract.InceptionDate)
            .Must(date => !date.HasValue || date.Value != default)
            .WithMessage("inceptionDate must be a valid date when provided.");
        RuleFor(contract => contract.ExpiryDate)
            .Must(date => !date.HasValue || date.Value != default)
            .WithMessage("expiryDate must be a valid date when provided.");
        RuleFor(contract => contract)
            .Must(contract => !contract.ExpiryDate.HasValue || !contract.InceptionDate.HasValue
                || contract.ExpiryDate.Value >= contract.InceptionDate.Value)
            .OverridePropertyName(nameof(UpsertFacultativeContractRequest.ExpiryDate))
            .WithMessage("expiryDate must be on or after inceptionDate.");

        RuleFor(contract => contract.WrittenSharePct).InclusiveBetween(0m, 100m).When(contract => contract.WrittenSharePct.HasValue);
        RuleFor(contract => contract.SignedSharePct).InclusiveBetween(0m, 100m).When(contract => contract.SignedSharePct.HasValue);
        RuleFor(contract => contract.CedentCommissionPct).InclusiveBetween(0m, 100m).When(contract => contract.CedentCommissionPct.HasValue);
        RuleFor(contract => contract)
            .Must(contract => !contract.WrittenSharePct.HasValue || !contract.SignedSharePct.HasValue
                || contract.SignedSharePct.Value <= contract.WrittenSharePct.Value)
            .OverridePropertyName(nameof(UpsertFacultativeContractRequest.SignedSharePct))
            .WithMessage("signedSharePct cannot exceed writtenSharePct.");

        RuleFor(contract => contract.SumInsured).GreaterThanOrEqualTo(0m).When(contract => contract.SumInsured.HasValue);
        RuleFor(contract => contract.LimitOfLiability).GreaterThanOrEqualTo(0m).When(contract => contract.LimitOfLiability.HasValue);
        RuleFor(contract => contract.Deductible).GreaterThanOrEqualTo(0m).When(contract => contract.Deductible.HasValue);
        RuleFor(contract => contract.PremiumRate)
            .InclusiveBetween(0m, 100m).When(contract => contract.PremiumRate.HasValue)
            .WithMessage("premiumRate must be between 0 and 100 because it is a percentage.");
        RuleFor(contract => contract.Fees)
            .GreaterThanOrEqualTo(0m).When(contract => contract.Fees.HasValue)
            .WithMessage("fees (IRIS commission) must be greater than or equal to 0.");
        RuleFor(contract => contract)
            .Must(contract =>
            {
                var calculation = FacultativeFinancialCalculator.Calculate(
                    contract.SumInsured, contract.PremiumRate, contract.Fees, contract.CedentCommissionPct);
                return calculation is null || calculation.GrossPremium * (contract.CedentCommissionPct ?? 0m) / 100m <= calculation.GrossPremium;
            })
            .OverridePropertyName(nameof(UpsertFacultativeContractRequest.CedentCommissionPct))
            .WithMessage("Cedent commission amount cannot exceed gross premium.");
        RuleFor(contract => contract)
            .Must(contract => !contract.SumInsured.HasValue || !contract.Deductible.HasValue
                || contract.Deductible.Value <= contract.SumInsured.Value)
            .OverridePropertyName(nameof(UpsertFacultativeContractRequest.Deductible))
            .WithMessage("deductible cannot exceed sumInsured.");

        RuleFor(contract => contract.FrontingReference).MaximumLength(100);
        RuleFor(contract => contract.CedentPremiumReference).MaximumLength(100);
        RuleFor(contract => contract.MemoText).MaximumLength(500);

        When(contract => contract.CoverageSelection is not null, () =>
        {
            RuleFor(contract => contract.CoverageSelection!.MainCoverId).GreaterThanOrEqualTo(0);
            RuleFor(contract => contract.CoverageSelection!.CoverageIds)
                .Must(ids => ids is null || ids.Count == 0 || ids.All(id => id > 0))
                .WithMessage("coverageSelection.coverageIds contains an invalid ID.");
            RuleFor(contract => contract.CoverageSelection!.SectionIds)
                .Must(ids => ids is null || ids.All(id => id > 0))
                .WithMessage("coverageSelection.sectionIds contains an invalid ID.");
            RuleFor(contract => contract.CoverageSelection!.ExtensionIds)
                .Must(ids => ids is null || ids.All(id => id > 0))
                .WithMessage("coverageSelection.extensionIds contains an invalid ID.");
        });
    }
}
