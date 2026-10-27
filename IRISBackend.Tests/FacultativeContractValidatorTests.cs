using IRISBackend.DTOs;
using IRISBackend.Validators;
using Xunit;

namespace IRISBackend.Tests
{
    public class FacultativeContractValidatorTests
    {
        private readonly UpsertFacultativeContractRequestValidator _validator = new();

        [Fact]
        public void Validate_WhenDraftPayloadIsEmpty_AcceptsItForInitialSave()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest());

            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
        }

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
        public void Validate_WhenFrontingReferenceIsMissingWhileFronting_AllowsIncompleteDraft()
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

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WhenCoverageSelectionIsMissing_AllowsIncompleteDraft()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31)
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WhenMainCoverIsMissingInsideSuppliedSelection_AllowsPartialDraft()
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

            Assert.True(result.IsValid);
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

        [Fact]
        public void Validate_WhenOptionalFinancialFieldsAreNull_AcceptsActualFacultativeRequestShape()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Facultative draft",
                CounterpartyId = 10,
                CurrencyId = 2,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                ReceptionDate = null,
                WrittenSharePct = null,
                SignedSharePct = null,
                Fees = null,
                SumInsured = null,
                LimitOfLiability = null,
                Deductible = null,
                PremiumRate = null,
                GrossPremium = null,
                CedentCommissionPct = null,
                CoverageSelection = new CoverageSelectionRequestDto
                {
                    PrimaryMainCoverId = 1,
                    CoverageIds = [1],
                    MainCoverId = 1,
                    SectionIds = [],
                    ExtensionIds = []
                }
            });

            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
        }

        [Fact]
        public void Validate_WhenRequiredDatabaseIdsAreMissing_IdentifiesTheirDtoFields()
        {
            var result = _validator.Validate(new UpsertFacultativeContractRequest
            {
                Name = "Facultative draft",
                CounterpartyId = 0,
                CurrencyId = 0,
                InceptionDate = new DateOnly(2026, 9, 1),
                ExpiryDate = new DateOnly(2027, 8, 31),
                CoverageSelection = new CoverageSelectionRequestDto { PrimaryMainCoverId = 0, MainCoverId = 0 }
            });

            Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpsertFacultativeContractRequest.CounterpartyId));
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpsertFacultativeContractRequest.CurrencyId));
        }

        [Fact]
        public void ContractValidationErrorResponse_GroupsErrorsByJsonFieldName()
        {
            var response = ContractValidationErrorResponseDto.From(
            [
                new ContractFieldError("CounterpartyId", "must be positive"),
                new ContractFieldError("CoverageSelection.PrimaryMainCoverId", "is required"),
                new ContractFieldError("CurrencyId", "was not found")
            ]);

            Assert.Equal("Contract validation failed.", response.Message);
            Assert.Equal("must be positive", Assert.Single(response.Errors["counterpartyId"]));
            Assert.Equal("is required", Assert.Single(response.Errors["coverageSelection.primaryMainCoverId"]));
            Assert.Equal("was not found", Assert.Single(response.Errors["currencyId"]));
        }
    }
}
