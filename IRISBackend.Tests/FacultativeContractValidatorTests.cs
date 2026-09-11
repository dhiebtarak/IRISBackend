using IRISBackend.DTOs;
using IRISBackend.Validators;
using Xunit;

namespace IRISBackend.Tests
{
    public class FacultativeContractValidatorTests
    {
        private readonly UpsertFacultativeContractRequestValidator _validator = new();

        [Fact]
        public void Validate_WhenDatesAreInvalid_ReturnsExpiryDateError()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2026, 8, 31)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "expiryDate must be on or after inceptionDate.");
        }

        [Fact]
        public void Validate_WhenPercentagesAreOutsideRange_ReturnsErrors()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                WrittenSharePct = 101,
                SignedSharePct = -1,
                CedentCommissionPct = 120
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpsertFacultativeContractRequest.WrittenSharePct));
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpsertFacultativeContractRequest.SignedSharePct));
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpsertFacultativeContractRequest.CedentCommissionPct));
        }

        [Fact]
        public void Validate_WhenSignedShareExceedsWrittenShare_ReturnsError()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                WrittenSharePct = 10,
                SignedSharePct = 15
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "signedSharePct cannot exceed writtenSharePct.");
        }

        [Fact]
        public void Validate_WhenFrontingReferenceIsMissingWhileFronting_ReturnsError()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                IsFronting = true
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "frontingReference is required when isFronting is true.");
        }

        [Fact]
        public void Validate_WhenCoverageSelectionIsMissing_ReturnsError()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "coverageSelection is required.");
        }

        [Fact]
        public void Validate_WhenMainCoverIsMissing_ReturnsError()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                CoverageSelection = new CoverageSelectionRequestDto()
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "coverageSelection.mainCoverId is required.");
        }

        [Fact]
        public void Validate_WhenLimitOfLiabilityIsNegative_ReturnsError()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                LimitOfLiability = -1m,
                CoverageSelection = new CoverageSelectionRequestDto { MainCoverId = 1 }
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpsertFacultativeContractRequest.LimitOfLiability));
        }
    }
}
