using System.Security.Claims;
using FluentValidation;
using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace IRISBackend.Controllers;

[ApiController]
[Route("api/admin/access-activity")]
[Authorize(Roles = IdentityRoles.Admin)]
public sealed class AccessActivityController(AccessActivityService activity, AuthSessionService sessions,
    IValidator<AccessActivityQuery> queryValidator, IValidator<RevokeSessionRequest> revokeValidator) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await activity.SummaryAsync(ct));
    [HttpGet("sessions")]
    public async Task<IActionResult> List([FromQuery] AccessActivityQuery query, CancellationToken ct)
    {
        var validation = await queryValidator.ValidateAsync(query, ct);
        if (!validation.IsValid) return BadRequest(new { message = "Invalid activity filters.", errors = validation.Errors.Select(e => e.ErrorMessage) });
        return Ok(await activity.ListAsync(query, User.FindFirstValue("sid"), ct));
    }
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> Detail(Guid sessionId, CancellationToken ct)
    {
        var result = await activity.DetailAsync(sessionId, User.FindFirstValue("sid"), ct);
        return result == null ? NotFound() : Ok(result);
    }
    [HttpGet("users/{userId}/history")]
    public async Task<IActionResult> History(string userId, [FromQuery] AccessActivityQuery query, CancellationToken ct)
    {
        var validation = await queryValidator.ValidateAsync(query, ct);
        if (!validation.IsValid) return BadRequest(new { message = "Invalid history filters.", errors = validation.Errors.Select(e => e.ErrorMessage) });
        return Ok(await activity.HistoryAsync(userId, query, ct));
    }
    [HttpPost("sessions/{sessionId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid sessionId, RevokeSessionRequest request, CancellationToken ct)
    {
        var validation = await revokeValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new { message = "A reason of 1-200 printable characters is required." });
        var detail = await activity.DetailAsync(sessionId, User.FindFirstValue("sid"), ct);
        if (detail == null) return NotFound();
        var count = await sessions.RevokeAsync(sessionId, null, User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.Reason.Trim(), ct);
        return count == 0 ? Conflict(new { message = "Only an active session can be revoked." }) : Ok(new { revokedCount = count, currentSessionRevoked = detail.IsCurrentSession });
    }
    [HttpPost("users/{userId}/revoke-all")]
    public async Task<IActionResult> RevokeAll(string userId, RevokeSessionRequest request, CancellationToken ct)
    {
        var validation = await revokeValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new { message = "A reason of 1-200 printable characters is required." });
        if (userId.Length > 450 || !await activity.UserExistsAsync(userId, ct)) return NotFound();
        var count = await sessions.RevokeAsync(null, userId, User.FindFirstValue(ClaimTypes.NameIdentifier)!, request.Reason.Trim(), ct);
        return Ok(new { revokedCount = count });
    }
}
