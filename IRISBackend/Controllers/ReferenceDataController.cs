using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/reference-data")]
    public sealed class ReferenceDataController : ApiControllerBase
    {
        private readonly IReferenceDataService _referenceDataService;
        private readonly IValidator<CedentLookupQueryDto> _cedentLookupValidator;

        public ReferenceDataController(
            IReferenceDataService referenceDataService,
            IValidator<CedentLookupQueryDto> cedentLookupValidator)
        {
            _referenceDataService = referenceDataService;
            _cedentLookupValidator = cedentLookupValidator;
        }

        [HttpGet("contract-types")]
        [ProducesResponseType(typeof(IEnumerable<ContractTypeReferenceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetContractTypes(CancellationToken cancellationToken)
        {
            var result = await _referenceDataService.GetContractTypesAsync(cancellationToken);
            return FromServiceResult(result);
        }

        [HttpGet("currencies")]
        [ProducesResponseType(typeof(IEnumerable<CurrencyReferenceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCurrencies(CancellationToken cancellationToken)
        {
            var result = await _referenceDataService.GetActiveCurrenciesAsync(cancellationToken);
            return FromServiceResult(result);
        }

        /// <summary>
        /// Returns active cedents for the facultative-contract autocomplete dropdown.
        /// </summary>
        /// <remarks>
        /// Examples:
        /// GET /api/reference-data/cedents?page=1&amp;pageSize=20
        /// GET /api/reference-data/cedents?search=ami&amp;page=1&amp;pageSize=20
        /// </remarks>
        [HttpGet("cedents")]
        [ProducesResponseType(typeof(PagedResponseDto<CedentLookupItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SearchCedents([FromQuery] CedentLookupQueryDto query, CancellationToken cancellationToken)
        {
            var validationResult = await _cedentLookupValidator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(new ApiErrorResponseDto
                {
                    Message = "Validation failed.",
                    Errors = validationResult.Errors.Select(error => error.ErrorMessage)
                });
            }

            var result = await _referenceDataService.SearchCedentsAsync(query, cancellationToken);
            return FromServiceResult(result);
        }
    }
}
