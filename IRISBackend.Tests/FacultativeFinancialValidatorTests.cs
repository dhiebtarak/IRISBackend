using FluentValidation.TestHelper;
using IRISBackend.DTOs;
using IRISBackend.Validators;
using Xunit;

namespace IRISBackend.Tests;

public sealed class FacultativeFinancialValidatorTests
{
    private readonly UpsertFacultativeContractRequestValidator _validator = new();

    [Fact]
    public void RejectsPercentageStylePremiumRateWhenCanonicalRateIsFractional()
    {
        var request = CreateRequest();
        request.PremiumRate = 2m;
        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(request => request.PremiumRate);
    }

    [Fact]
    public void RejectsCommissionsThatExceedGrossPremium()
    {
        var request = CreateRequest();
        request.Fees = 1900m;
        request.CedentCommissionPct = 10m;
        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor<UpsertFacultativeContractRequest>(request => request);
    }

    private static UpsertFacultativeContractRequest CreateRequest() => new()
    {
        Name = "Financial test",
        CounterpartyId = 1,
        CurrencyId = 1,
        InceptionDate = new DateOnly(2026, 1, 1),
        ExpiryDate = new DateOnly(2026, 12, 31),
        SumInsured = 100000m,
        PremiumRate = 0.02m,
        Fees = 200m,
        CedentCommissionPct = 5m,
        CoverageSelection = new CoverageSelectionRequestDto { MainCoverId = 1 }
    };
}
