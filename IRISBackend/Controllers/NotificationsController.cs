using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    private readonly IFinancialWorkflowService _service;
    public NotificationsController(IFinancialWorkflowService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool unreadOnly = false, CancellationToken cancellationToken = default) => FromServiceResult(await _service.GetNotificationsAsync(unreadOnly, cancellationToken));

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken) => FromServiceResult(await _service.GetUnreadNotificationCountAsync(cancellationToken));

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.MarkNotificationReadAsync(id, false, cancellationToken));

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken) => FromServiceResult(await _service.MarkNotificationReadAsync(0, true, cancellationToken));
}
