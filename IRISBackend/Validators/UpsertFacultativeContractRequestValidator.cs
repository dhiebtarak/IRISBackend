using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators
{
    public sealed class UpsertFacultativeContractRequestValidator : AbstractValidator<UpsertFacultativeContractRequest>
    {
        public UpsertFacultativeContractRequestValidator()
        {
            RuleFor(contract => contract.Name)
                .NotEmpty()
                .MaximumLength(255);

            RuleFor(contract => contract.CounterpartyId)
                .GreaterThan(0);

            RuleFor(contract => contract.CurrencyId)
                .GreaterThan(0);

            RuleFor(contract => contract.InceptionDate)
                .Must(date => date != default)
                .WithMessage("inceptionDate is required.");

            RuleFor(contract => contract.ExpiryDate)
                .Must(date => date != default)
                .WithMessage("expiryDate is required.");

            RuleFor(contract => contract)
                .Must(contract => contract.ExpiryDate >= contract.InceptionDate)
                .WithMessage("expiryDate must be on or after inceptionDate.");

            RuleFor(contract => contract.WrittenSharePct)
                .InclusiveBetween(0m, 100m)
                .When(contract => contract.WrittenSharePct.HasValue);

            RuleFor(contract => contract.SignedSharePct)
                .InclusiveBetween(0m, 100m)
                .When(contract => contract.SignedSharePct.HasValue);

            RuleFor(contract => contract.CedentCommissionPct)
                .InclusiveBetween(0m, 100m)
                .When(contract => contract.CedentCommissionPct.HasValue);

            RuleFor(contract => contract)
                .Must(contract =>
                    !contract.WrittenSharePct.HasValue ||
                    !contract.SignedSharePct.HasValue ||
                    contract.SignedSharePct.Value <= contract.WrittenSharePct.Value)
                .WithMessage("signedSharePct cannot exceed writtenSharePct.");

            RuleFor(contract => contract.Fees)
                .GreaterThanOrEqualTo(0m)
                .When(contract => contract.Fees.HasValue);

            RuleFor(contract => contract.SumInsured)
                .GreaterThanOrEqualTo(0m)
                .When(contract => contract.SumInsured.HasValue);

            RuleFor(contract => contract.LimitOfLiability)
                .GreaterThanOrEqualTo(0m)
                .When(contract => contract.LimitOfLiability.HasValue);

            RuleFor(contract => contract.Deductible)
                .GreaterThanOrEqualTo(0m)
                .When(contract => contract.Deductible.HasValue);

            RuleFor(contract => contract.PremiumRate)
                .GreaterThanOrEqualTo(0m)
                .When(contract => contract.PremiumRate.HasValue);

            RuleFor(contract => contract.GrossPremium)
                .GreaterThanOrEqualTo(0m)
                .When(contract => contract.GrossPremium.HasValue);

            RuleFor(contract => contract)
                .Must(contract =>
                    !contract.SumInsured.HasValue ||
                    !contract.Deductible.HasValue ||
                    contract.Deductible.Value <= contract.SumInsured.Value)
                .WithMessage("deductible cannot exceed sumInsured.");

            RuleFor(contract => contract.FrontingReference)
                .NotEmpty()
                .When(contract => contract.IsFronting)
                .WithMessage("frontingReference is required when isFronting is true.");

            RuleFor(contract => contract.FrontingReference)
                .MaximumLength(100);

            RuleFor(contract => contract.CedentPremiumReference)
                .MaximumLength(100);

            RuleFor(contract => contract.MemoText)
                .MaximumLength(500);

            RuleFor(contract => contract.CoverageSelection)
                .NotNull()
                .WithMessage("coverageSelection is required.");

            When(contract => contract.CoverageSelection is not null, () =>
            {
                RuleFor(contract => contract.CoverageSelection!.MainCoverId)
                    .GreaterThanOrEqualTo(0);

                RuleFor(contract => contract.CoverageSelection!.PrimaryMainCoverId)
                    .GreaterThan(0)
                    .When(contract => contract.CoverageSelection!.MainCoverId <= 0)
                    .WithMessage("coverageSelection.mainCoverId is required.");

                RuleFor(contract => contract.CoverageSelection!.CoverageIds)
                    .Must(ids => ids.Count == 0 || ids.All(id => id > 0))
                    .WithMessage("coverageSelection.coverageIds contains an invalid ID.");
            });
        }
    }
}
