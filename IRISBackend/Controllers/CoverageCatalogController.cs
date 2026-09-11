using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/coverage-catalog")]
    public sealed class CoverageCatalogController : ApiControllerBase
    {
        private readonly ICoverageCatalogService _coverageCatalogService;

        public CoverageCatalogController(ICoverageCatalogService coverageCatalogService)
        {
            _coverageCatalogService = coverageCatalogService;
        }

        /// <summary>
        /// Returns the complete active coverage catalog for local frontend filtering.
        /// </summary>
        [HttpGet("main-covers")]
        [ProducesResponseType(typeof(IEnumerable<CoverageCatalogItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetMainCovers(CancellationToken cancellationToken)
        {
            var result = await _coverageCatalogService.GetMainCoversAsync(cancellationToken);
            return FromServiceResult(result);
        }

        /// <summary>
        /// Returns active child sections or extensions for a selected coverage item.
        /// </summary>
        [HttpGet("{parentCoverageItemId:int}/children")]
        [ProducesResponseType(typeof(IEnumerable<CoverageCatalogItemDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetChildren(
            int parentCoverageItemId,
            [FromQuery] string? level,
            CancellationToken cancellationToken)
        {
            var result = await _coverageCatalogService.GetChildrenAsync(parentCoverageItemId, level, cancellationToken);
            return FromServiceResult(result);
        }
    }
}
