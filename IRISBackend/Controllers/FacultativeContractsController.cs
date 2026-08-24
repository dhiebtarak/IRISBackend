using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/contracts/facultative")]
    public sealed class FacultativeContractsController : ApiControllerBase
    {
        private readonly IFacultativeContractService _facultativeContractService;
        private readonly IValidator<UpsertFacultativeContractRequest> _validator;

        public FacultativeContractsController(
            IFacultativeContractService facultativeContractService,
            IValidator<UpsertFacultativeContractRequest> validator)
        {
            _facultativeContractService = facultativeContractService;
            _validator = validator;
        }

        [HttpPost]
        [Authorize(Roles = IdentityRoles.Employee)]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(FacultativeContractResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] UpsertFacultativeContractRequest request, CancellationToken cancellationToken)
        {
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(new ApiErrorResponseDto
                {
                    Message = "Validation failed.",
                    Errors = validationResult.Errors.Select(error => error.ErrorMessage)
                });
            }

            var result = await _facultativeContractService.CreateDraftAsync(request, cancellationToken);
            if (!result.Succeeded)
            {
                return FromServiceResult(result);
            }

            return CreatedAtAction(nameof(GetById), new { contractId = result.Value!.Id }, result.Value);
        }

        [HttpGet("{contractId:int}")]
        [ProducesResponseType(typeof(FacultativeContractResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(int contractId, CancellationToken cancellationToken)
        {
            var result = await _facultativeContractService.GetByIdAsync(contractId, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPut("{contractId:int}")]
        [Authorize(Roles = IdentityRoles.Employee)]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(FacultativeContractResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(int contractId, [FromBody] UpsertFacultativeContractRequest request, CancellationToken cancellationToken)
        {
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(new ApiErrorResponseDto
                {
                    Message = "Validation failed.",
                    Errors = validationResult.Errors.Select(error => error.ErrorMessage)
                });
            }

            var result = await _facultativeContractService.UpdateDraftAsync(contractId, request, cancellationToken);
            return FromServiceResult(result);
        }
    }
}
