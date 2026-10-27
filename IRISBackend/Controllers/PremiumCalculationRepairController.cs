using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize(Roles = IdentityRoles.Admin)]
[Route("api/admin/maintenance/facultative-panel-premiums")]
public sealed class PremiumCalculationRepairController(FacultativePanelFinancialRepairService repairService) : ApiControllerBase
{
    [HttpPost("recalculate")]
    [ProducesResponseType(typeof(PremiumCalculationRepairResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recalculate(CancellationToken cancellationToken)
    {
        var result = await repairService.RepairAsync(cancellationToken);
        return FromServiceResult(result);
    }
}
