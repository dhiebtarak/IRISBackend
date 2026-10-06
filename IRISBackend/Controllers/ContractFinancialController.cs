using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/contracts/{contractId:int}")]
public sealed class ContractFinancialController : ApiControllerBase
{
    private readonly IFinancialWorkflowService _service;
    public ContractFinancialController(IFinancialWorkflowService service) => _service = service;

    [HttpGet("financial-history")]
    public async Task<IActionResult> FinancialHistory(int contractId, CancellationToken cancellationToken) => FromServiceResult(await _service.GetFinancialHistoryAsync(contractId, cancellationToken));

    [HttpGet("invoice-context")]
    public async Task<IActionResult> InvoiceContext(int contractId, CancellationToken cancellationToken) => FromServiceResult(await _service.GetInvoiceContextAsync(contractId, cancellationToken));

    [HttpGet("/api/back-office/contracts/{contractId:int}/invoice-context")]
    public async Task<IActionResult> BackOfficeInvoiceContext(int contractId, CancellationToken cancellationToken) => FromServiceResult(await _service.GetInvoiceContextAsync(contractId, cancellationToken));

    [HttpPost("cedent-closings")]
    public async Task<IActionResult> CedentClosing(int contractId, [FromBody] CedentClosingRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateCedentClosingAsync(contractId, request, cancellationToken));

    [HttpPost("panels/{panelId:int}/invoices")]
    public async Task<IActionResult> PanelInvoice(int contractId, int panelId, [FromBody] InvoiceRequestDto request, CancellationToken cancellationToken)
    {
        request.ContractId = contractId;
        request.PanelId = panelId;
        return FromServiceResult(await _service.CreateInvoiceAsync(request, cancellationToken));
    }
}
