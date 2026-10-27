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
        private readonly ILogger<FacultativeContractsController> _logger;

        public FacultativeContractsController(
            IFacultativeContractService facultativeContractService,
            IValidator<UpsertFacultativeContractRequest> validator,
            ILogger<FacultativeContractsController> logger)
        {
            _facultativeContractService = facultativeContractService;
            _validator = validator;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Roles = IdentityRoles.Employee)]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(FacultativeContractResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ContractValidationErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] UpsertFacultativeContractRequest request, CancellationToken cancellationToken)
        {
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Facultative contract request rejected by FluentValidation. TraceId={TraceId}, Fields={Fields}",
                    HttpContext.TraceIdentifier,
                    string.Join(",", validationResult.Errors.Select(error => ContractValidationErrorResponseDto.NormalizeField(error.PropertyName, error.ErrorMessage)).Distinct()));
                return BadRequest(ContractValidationErrorResponseDto.From(validationResult.Errors.Select(error =>
                    new ContractFieldError(error.PropertyName, error.ErrorMessage))));
            }

            var result = await _facultativeContractService.CreateDraftAsync(request, cancellationToken);
            if (!result.Succeeded)
            {
                if (result.StatusCode == StatusCodes.Status400BadRequest)
                {
                    _logger.LogWarning("Facultative contract business validation failed. TraceId={TraceId}, Fields={Fields}",
                        HttpContext.TraceIdentifier,
                        string.Join(",", (result.Error?.Errors ?? []).Select(ContractValidationErrorResponseDto.InferField).Distinct()));
                    return BadRequest(ContractValidationErrorResponseDto.From((result.Error?.Errors ?? [])
                        .Select(message => new ContractFieldError(ContractValidationErrorResponseDto.InferField(message), message))));
                }
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
        [ProducesResponseType(typeof(ContractValidationErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(int contractId, [FromBody] UpsertFacultativeContractRequest request, CancellationToken cancellationToken)
        {
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Facultative contract update rejected by FluentValidation. TraceId={TraceId}, Fields={Fields}",
                    HttpContext.TraceIdentifier,
                    string.Join(",", validationResult.Errors.Select(error => ContractValidationErrorResponseDto.NormalizeField(error.PropertyName, error.ErrorMessage)).Distinct()));
                return BadRequest(ContractValidationErrorResponseDto.From(validationResult.Errors.Select(error =>
                    new ContractFieldError(error.PropertyName, error.ErrorMessage))));
            }

            var result = await _facultativeContractService.UpdateDraftAsync(contractId, request, cancellationToken);
            if (!result.Succeeded && result.StatusCode == StatusCodes.Status400BadRequest)
            {
                _logger.LogWarning("Facultative contract update business validation failed. TraceId={TraceId}, Fields={Fields}",
                    HttpContext.TraceIdentifier,
                    string.Join(",", (result.Error?.Errors ?? []).Select(ContractValidationErrorResponseDto.InferField).Distinct()));
                return BadRequest(ContractValidationErrorResponseDto.From((result.Error?.Errors ?? [])
                    .Select(message => new ContractFieldError(ContractValidationErrorResponseDto.InferField(message), message))));
            }
            return FromServiceResult(result);
        }
    }
}
