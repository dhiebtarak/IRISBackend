using IRISBackend.DTOs;
using IRISBackend.Validators;
using Xunit;

namespace IRISBackend.Tests
{
    public sealed class ContractListQueryValidatorTests
    {
        private readonly ContractListQueryDtoValidator _validator = new();

        [Fact]
        public void Validate_WhenPageSizeIsTooLarge_ReturnsClearError()
        {
            var result = _validator.Validate(new ContractListQueryDto { PageSize = 101 });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "pageSize must be between 1 and 100.");
        }

        [Fact]
        public void Validate_WhenDateRangesAreReversed_ReturnsBothErrors()
        {
            var result = _validator.Validate(new ContractListQueryDto
            {
                InceptionFrom = new DateOnly(2026, 12, 31),
                InceptionTo = new DateOnly(2026, 1, 1),
                ExpiryFrom = new DateOnly(2027, 12, 31),
                ExpiryTo = new DateOnly(2027, 1, 1)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "inceptionFrom must be on or before inceptionTo.");
            Assert.Contains(result.Errors, error => error.ErrorMessage == "expiryFrom must be on or before expiryTo.");
        }

        [Fact]
        public void Validate_WhenPlacementInProgressIsUsed_AllowsTheStatusFilter()
        {
            var result = _validator.Validate(new ContractListQueryDto
            {
                Status = "PlacementInProgress"
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WhenUnknownStatusIsUsed_ReturnsClearError()
        {
            var result = _validator.Validate(new ContractListQueryDto
            {
                Status = "UnknownStatus"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.ErrorMessage == "status is invalid.");
        }
    }
}
