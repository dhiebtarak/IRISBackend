using IRISBackend.DTOs;
using IRISBackend.Validators;
using Xunit;

namespace IRISBackend.Tests
{
    public sealed class ContractPanelRequestValidatorTests
    {
        private readonly ContractPanelRequestDtoValidator _validator = new();

        [Fact]
        public void Validate_WhenSignedSharePctIsMissing_ReturnsRequiredError()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                IdCounterparty = 1,
                WrittenSharePct = 25m,
                Status = "Draft"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error =>
                error.PropertyName == nameof(ContractPanelRequestDto.SignedSharePct) &&
                error.ErrorMessage == "signedSharePct is required.");
        }

        [Fact]
        public void Validate_WhenCounterpartyIdIsMissing_ReturnsSelectionError()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                SignedSharePct = 20m,
                Status = "Draft"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error =>
                error.PropertyName == nameof(ContractPanelRequestDto.IdCounterparty) &&
                error.ErrorMessage == "idCounterparty must be the selected reinsurer id.");
        }

        [Fact]
        public void Validate_WhenOtherDeductionsIsNegative_ReturnsClearError()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                IdCounterparty = 1,
                WrittenSharePct = 25m,
                SignedSharePct = 20m,
                OtherDeductions = -1m,
                Status = "Draft"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error =>
                error.PropertyName == nameof(ContractPanelRequestDto.OtherDeductions) &&
                error.ErrorMessage == "otherDeductions must be greater than or equal to 0.");
        }

        [Fact]
        public void Validate_WhenWrittenSharePctIsZero_ReturnsPositiveShareError()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                IdCounterparty = 1,
                WrittenSharePct = 0m,
                SignedSharePct = 0m,
                Status = "Draft"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error =>
                error.PropertyName == nameof(ContractPanelRequestDto.WrittenSharePct) &&
                error.ErrorMessage == "writtenSharePct must be greater than 0 and no more than 100.");
        }

        [Fact]
        public void Validate_WhenRequestIsValid_Passes()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                IdCounterparty = 1,
                WrittenSharePct = 25m,
                SignedSharePct = 20m,
                Tsi = 1000000m,
                Deductible = 5000m,
                PremiumRatePct = 1.5m,
                GrossPremium = 15000m,
                BrokeragePct = 10m,
                OtherDeductions = 0m,
                PremiumReference = "REF-REINS-001",
                Status = "Draft"
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_AcceptsFractionalOtherDeductionsPercentage()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                IdCounterparty = 1,
                WrittenSharePct = 25m,
                SignedSharePct = 20m,
                OtherDeductionsPercentage = 2.75m,
                Status = "Draft"
            });

            Assert.DoesNotContain(result.Errors, error => error.PropertyName == nameof(ContractPanelRequestDto.OtherDeductionsPercentage));
        }

        [Fact]
        public void Validate_RejectsOtherDeductionsPercentageAboveOneHundred()
        {
            var result = _validator.Validate(new ContractPanelRequestDto
            {
                IdCounterparty = 1,
                WrittenSharePct = 25m,
                SignedSharePct = 20m,
                OtherDeductionsPercentage = 100.01m,
                Status = "Draft"
            });

            Assert.Contains(result.Errors, error =>
                error.PropertyName == nameof(ContractPanelRequestDto.OtherDeductionsPercentage) &&
                error.ErrorMessage == "otherDeductionsPercentage must be between 0 and 100.");
        }
    }
}
