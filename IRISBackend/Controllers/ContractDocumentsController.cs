using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize(Roles = IdentityRoles.Employee)]
[Route("api/contracts/{contractId:int}")]
public sealed class ContractDocumentsController : ApiControllerBase
{
    private readonly IDocumentAttachmentService _documentService;

    public ContractDocumentsController(IDocumentAttachmentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet("documents")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentAttachmentResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListContractDocuments(int contractId, CancellationToken cancellationToken)
    {
        var result = await _documentService.ListContractDocumentsAsync(contractId, cancellationToken);
        return FromServiceResult(result);
    }

    [HttpPost("documents")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DocumentAttachmentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadContractDocument(
        int contractId,
        [FromBody] DocumentUploadRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _documentService.UploadContractDocumentAsync(contractId, request, cancellationToken);
        return FromServiceResult(result);
    }

    [HttpGet("panels/{panelId:int}/documents")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentAttachmentResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPanelDocuments(int contractId, int panelId, CancellationToken cancellationToken)
    {
        var result = await _documentService.ListPanelDocumentsAsync(contractId, panelId, cancellationToken);
        return FromServiceResult(result);
    }

    [HttpPost("panels/{panelId:int}/documents")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DocumentAttachmentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPanelDocument(
        int contractId,
        int panelId,
        [FromBody] DocumentUploadRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _documentService.UploadPanelDocumentAsync(contractId, panelId, request, cancellationToken);
        return FromServiceResult(result);
    }
}
