using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize(Roles = IdentityRoles.Employee)]
    [Route("api/contracts")]
    public sealed class ContractsController : ApiControllerBase
    {
        private readonly IContractReadService _contractReadService;
        private readonly IValidator<ContractListQueryDto> _queryValidator;

        public ContractsController(
            IContractReadService contractReadService,
            IValidator<ContractListQueryDto> queryValidator)
        {
            _contractReadService = contractReadService;
            _queryValidator = queryValidator;
        }

        /// <summary>Returns contracts accessible to the authenticated Front Officer.</summary>
        /// <remarks>
        /// Example: GET /api/contracts?search=FAC-2026&amp;contractType=FACULTATIVE&amp;status=Draft&amp;pageNumber=1&amp;pageSize=20&amp;sortBy=updatedAt&amp;sortDirection=desc
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResponseDto<ContractListItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> List([FromQuery] ContractListQueryDto query, CancellationToken cancellationToken)
        {
            var validationResult = await _queryValidator.ValidateAsync(query, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(new ApiErrorResponseDto
                {
                    Message = "Validation failed.",
                    Errors = validationResult.Errors.Select(error => error.ErrorMessage)
                });
            }

            var result = await _contractReadService.ListAsync(query, cancellationToken);
            return FromServiceResult(result);
        }

        /// <summary>Returns dashboard statistics for the authenticated Front Officer.</summary>
        [HttpGet("dashboard-summary")]
        [ProducesResponseType(typeof(ContractDashboardSummaryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetDashboardSummary(CancellationToken cancellationToken)
        {
            var result = await _contractReadService.GetDashboardSummaryAsync(cancellationToken);
            return FromServiceResult(result);
        }
    }
}
