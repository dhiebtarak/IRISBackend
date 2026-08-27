using IRISBackend.DTOs;
using IRISBackend.Validators;
using Xunit;

namespace IRISBackend.Tests
{
    public class CounterpartyValidatorTests
    {
        [Fact]
        public void CreateValidator_WhenRequiredFieldsAreMissing_ReturnsErrors()
        {
            var validator = CreateCreateValidator();

            var result = validator.Validate(new CounterpartyCreateDto
            {
                Status = string.Empty
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(CounterpartyCreateDto.Name));
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(CounterpartyCreateDto.Type));
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(CounterpartyCreateDto.Status));
        }

        [Fact]
        public void CreateValidator_WhenEmailIsInvalid_ReturnsEmailError()
        {
            var validator = CreateCreateValidator();

            var result = validator.Validate(new CounterpartyCreateDto
            {
                Name = "Counterparty",
                Type = "Broker",
                Email = "invalid-email",
                Status = "Active"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(CounterpartyCreateDto.Email));
        }

        [Fact]
        public void UpdateValidator_WhenRequestIsValid_Passes()
        {
            var validator = CreateUpdateValidator();

            var result = validator.Validate(new CounterpartyUpdateDto
            {
                LegacyClientId = 1001,
                LegacyCompanyCode = "CP-001",
                Name = "Counterparty",
                Type = "Broker",
                Email = "contact@example.com",
                PostalCode = "20000",
                Website = "https://example.com",
                Activity = "Insurance",
                CommercialRegister = CreateAttachmentUploadDto("commercial-register.pdf", "application/pdf", "QUJD"),
                CedantLegalEntity = "Cedant SA",
                Observations = "Valid observations",
                TaxIdAttachment = CreateAttachmentUploadDto("tax-id.pdf", "application/pdf", "REVG"),
                Status = "Active"
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AttachmentValidator_WhenFileNameIsMissing_ReturnsError()
        {
            var validator = CreateCreateValidator();

            var result = validator.Validate(new CounterpartyCreateDto
            {
                Name = "Counterparty",
                Type = "Broker",
                Status = "Active",
                CommercialRegister = new AttachmentUploadDto
                {
                    ContentType = "application/pdf",
                    Base64Content = "QUJD"
                }
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == "CommercialRegister.FileName");
        }

        [Fact]
        public void AttachmentValidator_WhenBase64ContentIsMissing_ReturnsError()
        {
            var validator = CreateCreateValidator();

            var result = validator.Validate(new CounterpartyCreateDto
            {
                Name = "Counterparty",
                Type = "Broker",
                Status = "Active",
                TaxIdAttachment = new AttachmentUploadDto
                {
                    FileName = "tax-id.pdf",
                    ContentType = "application/pdf"
                }
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == "TaxIdAttachment.Base64Content");
        }

        [Fact]
        public void QueryValidator_WhenPageSizeIsTooLarge_ReturnsError()
        {
            var validator = new CounterpartyQueryDtoValidator();

            var result = validator.Validate(new CounterpartyQueryDto
            {
                PageNumber = 1,
                PageSize = 101
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(CounterpartyQueryDto.PageSize));
            Assert.Contains(result.Errors, error => error.ErrorMessage == "pageSize must be between 1 and 100.");
        }

        [Fact]
        public void QueryDto_WhenConstructed_UsesSafePaginationDefaults()
        {
            var query = new CounterpartyQueryDto();

            Assert.Equal(1, query.PageNumber);
            Assert.Equal(25, query.PageSize);
        }

        [Fact]
        public void QueryValidator_WhenPageNumberIsLessThanOne_ReturnsNamedError()
        {
            var validator = new CounterpartyQueryDtoValidator();

            var result = validator.Validate(new CounterpartyQueryDto
            {
                PageNumber = 0,
                PageSize = 25
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.PropertyName == nameof(CounterpartyQueryDto.PageNumber));
            Assert.Contains(result.Errors, error => error.ErrorMessage == "pageNumber must be greater than or equal to 1.");
        }

        private static CounterpartyCreateDtoValidator CreateCreateValidator()
        {
            return new CounterpartyCreateDtoValidator();
        }

        private static CounterpartyUpdateDtoValidator CreateUpdateValidator()
        {
            return new CounterpartyUpdateDtoValidator();
        }

        private static AttachmentUploadDto CreateAttachmentUploadDto(string fileName, string contentType, string base64Content)
        {
            return new AttachmentUploadDto
            {
                FileName = fileName,
                ContentType = contentType,
                Base64Content = base64Content
            };
        }
    }
}
