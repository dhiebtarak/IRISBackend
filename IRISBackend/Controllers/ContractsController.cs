using FluentValidation;
using FluentValidation.Results;
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
        private readonly IContractPlacementService _contractPlacementService;
        private readonly IValidator<ContractPanelRequestDto> _panelValidator;

        public ContractsController(
            IContractReadService contractReadService,
            IValidator<ContractListQueryDto> queryValidator,
            IContractPlacementService contractPlacementService,
            IValidator<ContractPanelRequestDto> panelValidator)
        {
            _contractReadService = contractReadService;
            _queryValidator = queryValidator;
            _contractPlacementService = contractPlacementService;
            _panelValidator = panelValidator;
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
                return BadRequest(ToValidationErrorResponse(validationResult.Errors));
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

        /// <summary>Returns the contract overview used by the Front Officer overview page.</summary>
        [HttpGet("{contractId:int}/overview")]
        [ProducesResponseType(typeof(ContractOverviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetOverview(int contractId, CancellationToken cancellationToken)
        {
            var result = await _contractReadService.GetOverviewAsync(contractId, cancellationToken);
            return FromServiceResult(result);
        }

        /// <summary>Downloads the active slip template files for the selected contract coverages.</summary>
        [HttpGet("{contractId:int}/slips/download")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(SlipDownloadUnavailableDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DownloadSlips(int contractId, CancellationToken cancellationToken)
        {
            var result = await _contractReadService.DownloadSlipsAsync(contractId, cancellationToken);
            if (!result.Succeeded)
            {
                return FromServiceResult(result);
            }

            if (!result.Value!.Available)
            {
                return Ok(new SlipDownloadUnavailableDto
                {
                    Available = false,
                    Message = result.Value.Message ?? "Slip is not available for this contract selection."
                });
            }

            return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
        }

        /// <summary>Returns reinsurer panels linked to the contract placement.</summary>
        [HttpGet("{contractId:int}/panels")]
        [ProducesResponseType(typeof(IReadOnlyList<ContractPanelResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPanels(int contractId, CancellationToken cancellationToken)
        {
            var result = await _contractPlacementService.GetPanelsAsync(contractId, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPost("{contractId:int}/panels")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(ContractOverviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddPanel(
            int contractId,
            [FromBody] ContractPanelRequestDto request,
            CancellationToken cancellationToken)
        {
            var validationResult = await _panelValidator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(ToValidationErrorResponse(validationResult.Errors));
            }

            var result = await _contractPlacementService.AddPanelAsync(contractId, request, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPut("{contractId:int}/panels/{panelId:int}")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(ContractOverviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdatePanel(
            int contractId,
            int panelId,
            [FromBody] ContractPanelRequestDto request,
            CancellationToken cancellationToken)
        {
            var validationResult = await _panelValidator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(ToValidationErrorResponse(validationResult.Errors));
            }

            var result = await _contractPlacementService.UpdatePanelAsync(contractId, panelId, request, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPost("{contractId:int}/panels/{panelId:int}/confirm")]
        [ProducesResponseType(typeof(ContractPanelResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ConfirmPanel(
            int contractId,
            int panelId,
            CancellationToken cancellationToken)
        {
            var result = await _contractPlacementService.ConfirmPanelAsync(contractId, panelId, cancellationToken);
            return FromServiceResult(result);
        }

        [HttpPost("{contractId:int}/start-binding")]
        [ProducesResponseType(typeof(ContractOverviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StartBinding(int contractId, CancellationToken cancellationToken)
        {
            var result = await _contractPlacementService.StartBindingAsync(contractId, cancellationToken);
            return FromServiceResult(result);
        }

        private static ApiErrorResponseDto ToValidationErrorResponse(IEnumerable<ValidationFailure> errors)
        {
            return new ApiErrorResponseDto
            {
                Message = "Validation failed.",
                Errors = errors.Select(error =>
                {
                    var propertyName = ToCamelCase(error.PropertyName);
                    return string.IsNullOrWhiteSpace(propertyName)
                        ? error.ErrorMessage
                        : $"{propertyName}: {error.ErrorMessage}";
                })
            };
        }

        private static string ToCamelCase(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : char.ToLowerInvariant(value[0]) + value[1..];
        }
    }
}
