using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/counterparties")]
    public class CounterpartiesController : ApiControllerBase
    {
        private readonly ICounterpartyService _counterpartyService;
        private readonly IValidator<CounterpartyCreateDto> _createValidator;
        private readonly IValidator<CounterpartyUpdateDto> _updateValidator;
        private readonly IValidator<CounterpartyQueryDto> _queryValidator;

        public CounterpartiesController(
            ICounterpartyService counterpartyService,
            IValidator<CounterpartyCreateDto> createValidator,
            IValidator<CounterpartyUpdateDto> updateValidator,
            IValidator<CounterpartyQueryDto> queryValidator)
        {
            _counterpartyService = counterpartyService;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _queryValidator = queryValidator;
        }

        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(CounterpartyDetailsDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CounterpartyCreateDto request, CancellationToken cancellationToken)
        {
            var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(ToErrorResponse(validationResult.Errors.Select(error => error.ErrorMessage)));
            }

            var result = await _counterpartyService.CreateAsync(request, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(CounterpartyDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            var result = await _counterpartyService.GetByIdAsync(id, cancellationToken);
            return FromServiceResult(result);
        }

        /// <summary>Returns a filtered and paginated Counterparty list.</summary>
        /// <remarks>
        /// Example: GET /api/counterparties?search=axa&amp;type=Cedent&amp;status=Active&amp;country=Tunisie&amp;city=Tunis&amp;pageNumber=1&amp;pageSize=25&amp;sortBy=name&amp;sortDirection=asc
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResponseDto<CounterpartyListItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> List([FromQuery] CounterpartyQueryDto query, CancellationToken cancellationToken)
        {
            var validationResult = await _queryValidator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(ToErrorResponse(validationResult.Errors.Select(error => error.ErrorMessage)));
            }

            var result = await _counterpartyService.ListAsync(query, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPut("{id:int}")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(CounterpartyDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(int id, [FromBody] CounterpartyUpdateDto request, CancellationToken cancellationToken)
        {
            var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(ToErrorResponse(validationResult.Errors.Select(error => error.ErrorMessage)));
            }

            var result = await _counterpartyService.UpdateAsync(id, request, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(CounterpartyDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetActiveStatus(int id, CounterpartyStatusUpdateDto request, CancellationToken cancellationToken)
        {
            var result = await _counterpartyService.SetActiveStatusAsync(id, request.IsActive, cancellationToken);
            return FromServiceResult(result);
        }

        private static ApiErrorResponseDto ToErrorResponse(IEnumerable<string> errors)
        {
            return new ApiErrorResponseDto
            {
                Message = "Validation failed.",
                Errors = errors
            };
        }
    }
}
