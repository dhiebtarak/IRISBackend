using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController, Authorize, Route("api/claims/{claimId:int}/attachments")]
public sealed class ClaimDocumentsController(IDocumentAttachmentService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int claimId, CancellationToken ct) =>
        FromServiceResult(await service.ListClaimDocumentsAsync(claimId, ct));

    [HttpPost, Consumes("application/json")]
    public async Task<IActionResult> Upload(int claimId, DocumentUploadRequestDto request, CancellationToken ct) =>
        FromServiceResult(await service.UploadClaimDocumentAsync(claimId, request, ct));

    [HttpGet("{documentId:int}/download")]
    public async Task<IActionResult> Download(int claimId, int documentId, CancellationToken ct)
    {
        var result = await service.DownloadClaimDocumentAsync(claimId, documentId, ct);
        if (!result.Succeeded) return FromServiceResult(result);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.MimeType, result.Value.FileName, enableRangeProcessing: true);
    }

    [HttpDelete("{documentId:int}")]
    public async Task<IActionResult> Archive(int claimId, int documentId, CancellationToken ct) =>
        FromServiceResult(await service.ArchiveClaimDocumentAsync(claimId, documentId, ct));
}
