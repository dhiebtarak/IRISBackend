using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/back-office")]
public sealed class BackOfficeController : ApiControllerBase
{
    private readonly IFinancialWorkflowService _service;
    public BackOfficeController(IFinancialWorkflowService service)
    {
        _service = service;
    }

    [HttpGet("bank-receipts")]
    public async Task<IActionResult> Receipts([FromQuery] FinancialReceiptQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.ListReceiptsAsync(query, true, cancellationToken));

    [HttpGet("bank-receipts/{id:int}")]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.GetReceiptAsync(id, cancellationToken));

    [HttpGet("bank-receipts/{id:int}/eligible-invoices")]
    public async Task<IActionResult> EligibleInvoices(int id, [FromQuery] string? search, CancellationToken cancellationToken) => FromServiceResult(await _service.GetEligibleInvoicesAsync(id, search, cancellationToken));

    [HttpPost("bank-receipts/{id:int}/invoice-allocations")]
    public async Task<IActionResult> AllocateInvoices(int id, [FromBody] InvoiceReceiptAllocationRequestDto request, CancellationToken cancellationToken)
    {
        if (request?.Allocations is not { Count: > 0 })
        {
            return BadRequest(new ApiErrorResponseDto
            {
                Message = "Validation failed.",
                Errors = ["allocations: At least one invoice allocation is required."]
            });
        }

        return FromServiceResult(await _service.AllocateReceiptInvoicesAsync(id, request, cancellationToken));
    }

    [HttpGet("remittances")]
    public async Task<IActionResult> Remittances(CancellationToken cancellationToken) => FromServiceResult(await _service.ListRemittancesAsync(cancellationToken));

    [HttpPost("remittances/{remittanceRef:int}/confirm")]
    public async Task<IActionResult> ConfirmRemittance(int remittanceRef, CancellationToken cancellationToken) => FromServiceResult(await _service.ConfirmRemittanceAsync(remittanceRef, cancellationToken));

    [HttpPost("remittances/{remittanceRef:int}/confirm-for-disbursement")]
    public async Task<IActionResult> ConfirmRemittanceForDisbursement(int remittanceRef, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ConfirmRemittanceForDisbursementAsync(remittanceRef, cancellationToken));

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices([FromQuery] InvoiceQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.ListInvoicesAsync(query, cancellationToken));

    [HttpPost("invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] InvoiceRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateInvoiceAsync(request, cancellationToken));

    [HttpGet("invoices/{id:int}")]
    public async Task<IActionResult> Invoice(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.GetInvoiceAsync(id, cancellationToken));

    [HttpGet("contracts/{contractId:int}/invoices")]
    public async Task<IActionResult> ContractInvoices(int contractId, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ListContractInvoicesAsync(contractId, cancellationToken));

    [HttpGet("contracts/{contractId:int}/debit-note")]
    public async Task<IActionResult> ContractDebitNote(int contractId, CancellationToken cancellationToken)
    {
        var result = await _service.GetContractDebitNoteAsync(contractId, cancellationToken);
        if (!result.Succeeded) return FromServiceResult(result);
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("contracts/{contractId:int}/invoices/preview")]
    public async Task<IActionResult> PreviewPremiumInvoice(int contractId, [FromBody] PremiumDebitNoteRequestDto request, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.PreviewPremiumInvoiceAsync(contractId, request, cancellationToken));

    [HttpPost("contracts/{contractId:int}/invoices")]
    public async Task<IActionResult> CreatePremiumInvoice(int contractId, [FromBody] PremiumDebitNoteRequestDto request, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.CreatePremiumInvoiceAsync(contractId, request, cancellationToken));

    [HttpGet("invoices/{invoiceId:int}/document")]
    [HttpGet("invoices/{invoiceId:int}/debit-note")]
    public async Task<IActionResult> PremiumInvoiceDocument(int invoiceId, CancellationToken cancellationToken)
    {
        var result = await _service.GetPremiumInvoiceDocumentAsync(invoiceId, cancellationToken);
        if (!result.Succeeded) return FromServiceResult(result);
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
}
