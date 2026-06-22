using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/workspaces")]
    public class WorkspacesController : ApiControllerBase
    {
        private readonly IWorkspaceAccessService _workspaceAccessService;

        public WorkspacesController(IWorkspaceAccessService workspaceAccessService)
        {
            _workspaceAccessService = workspaceAccessService;
        }

        [HttpGet("me")]
        [ProducesResponseType(typeof(WorkspaceAccessResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Me()
        {
            var result = await _workspaceAccessService.GetCurrentWorkspaceAsync(User);
            return FromServiceResult(result);
        }
    }
}
