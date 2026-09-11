using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize(Roles = IdentityRoles.Employee)]
[Route("api/documents")]
public sealed class DocumentsController : ApiControllerBase
{
    private readonly IDocumentAttachmentService _documentService;

    public DocumentsController(IDocumentAttachmentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet("{documentId:int}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.DownloadAsync(documentId, cancellationToken);
        if (!result.Succeeded)
        {
            return FromServiceResult(result);
        }

        return File(result.Value!.Content, result.Value.MimeType, result.Value.FileName, enableRangeProcessing: true);
    }

    [HttpDelete("{documentId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(int documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.ArchiveAsync(documentId, cancellationToken);
        if (!result.Succeeded)
        {
            return FromServiceResult(result);
        }

        return NoContent();
    }
}
