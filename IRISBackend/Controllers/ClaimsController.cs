using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController, Authorize, Route("api/claims")]
public sealed class ClaimsController(IFinancialWorkflowService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int? contractId, int page = 1, int pageSize = 20, bool soaOnly = false, [FromQuery] ClaimListQueryDto? filters = null, CancellationToken ct = default) =>
        FromServiceResult(await service.ListClaimsAsync(contractId, page, pageSize, soaOnly, ct, filters));
    [HttpGet("{claimId:int}")]
    public async Task<IActionResult> Detail(int claimId, CancellationToken ct) => FromServiceResult(await service.GetClaimAsync(claimId, ct));
    [HttpGet("{claimId:int}/invoicing/preview"), Authorize(Policy = "Module:BackOffice")]
    public async Task<IActionResult> Preview(int claimId, CancellationToken ct) => FromServiceResult(await service.PreviewClaimInvoicingAsync(claimId, ct));
    [HttpPost("{claimId:int}/invoicing/approve"), Authorize(Policy = "Module:BackOffice")]
    public async Task<IActionResult> Approve(int claimId, ClaimInvoicingRequestDto request, CancellationToken ct) => FromServiceResult(await service.ApproveClaimInvoicingAsync(claimId, request, ct));
    [HttpGet("{claimId:int}/invoicing")]
    public async Task<IActionResult> Documents(int claimId, CancellationToken ct) => FromServiceResult(await service.GetClaimInvoicingAsync(claimId, ct));
    [HttpGet("{claimId:int}/invoicing/documents")]
    public async Task<IActionResult> Download(int claimId, string kind = "bundle", int? noteId = null, CancellationToken ct = default)
    {
        var result = await service.GetClaimInvoicingDocumentAsync(claimId, kind, noteId, ct);
        if (!result.Succeeded) return FromServiceResult(result);
        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
    [HttpGet("contracts/{contractId:int}/context")]
    public async Task<IActionResult> Context(int contractId, CancellationToken ct) => FromServiceResult(await service.GetClaimContextAsync(contractId, ct));
    [HttpPost("contracts/{contractId:int}")]
    [Authorize(Policy = "Module:Claims")]
    public async Task<IActionResult> Create(int contractId, ClaimRequestDto request, CancellationToken ct) => FromServiceResult(await service.SaveClaimAsync(contractId, null, request, ct));
    [HttpPut("contracts/{contractId:int}/{claimId:int}")]
    [Authorize(Policy = "Module:Claims")]
    public async Task<IActionResult> Update(int contractId, int claimId, ClaimRequestDto request, CancellationToken ct) => FromServiceResult(await service.SaveClaimAsync(contractId, claimId, request, ct));
    [HttpPost("{claimId:int}/collection-note")]
    [Authorize(Policy = "Module:BackOffice")]
    public async Task<IActionResult> Issue(int claimId, ClaimIssueRequestDto request, CancellationToken ct) => FromServiceResult(await service.IssueClaimCollectionAsync(claimId, request, ct));
    [HttpGet("{claimId:int}/collection-note/pdf")]
    public async Task<IActionResult> Pdf(int claimId, CancellationToken ct)
    {
        var result = await service.GetClaimCollectionDocumentAsync(claimId, ct);
        if (!result.Succeeded) return FromServiceResult(result);
        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
    [HttpGet("{claimId:int}/receipts")]
    [Authorize(Policy = "Module:BackOffice")]
    public async Task<IActionResult> Receipts(int claimId, CancellationToken ct) => FromServiceResult(await service.GetClaimReceiptsAsync(claimId, ct));
    [HttpPost("{claimId:int}/pair")]
    [Authorize(Policy = "Module:BackOffice")]
    public async Task<IActionResult> Pair(int claimId, ClaimPairRequestDto request, CancellationToken ct) => FromServiceResult(await service.PairClaimAsync(claimId, request, ct));
    [HttpPost("{claimId:int}/close")]
    public async Task<IActionResult> Close(int claimId, CancellationToken ct) => FromServiceResult(await service.CloseClaimAsync(claimId, ct));
}
