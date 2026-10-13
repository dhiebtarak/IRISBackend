using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize(Roles = IdentityRoles.Employee)]
[Route("api/bank-receipts/{receiptId:int}/attachments")]
public sealed class BankReceiptDocumentsController : ApiControllerBase
{
    private readonly IDocumentAttachmentService _documentService;

    public BankReceiptDocumentsController(IDocumentAttachmentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentAttachmentResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(int receiptId, CancellationToken cancellationToken)
    {
        var result = await _documentService.ListBankReceiptDocumentsAsync(receiptId, cancellationToken);
        return FromServiceResult(result);
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DocumentAttachmentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadDocument(
        int receiptId,
        [FromBody] DocumentUploadRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _documentService.UploadBankReceiptDocumentAsync(receiptId, request, cancellationToken);
        return FromServiceResult(result);
    }

    [HttpGet("{documentId:int}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int receiptId, int documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.DownloadBankReceiptDocumentAsync(receiptId, documentId, cancellationToken);
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
    public async Task<IActionResult> Archive(int receiptId, int documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.ArchiveBankReceiptDocumentAsync(receiptId, documentId, cancellationToken);
        if (!result.Succeeded)
        {
            return FromServiceResult(result);
        }

        return NoContent();
    }
}
