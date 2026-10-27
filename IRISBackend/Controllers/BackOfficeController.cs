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

        var result = await _service.AllocateReceiptInvoicesAsync(id, request, cancellationToken);
        if (!result.Succeeded && result.Error?.Code is { } code && result.StatusCode is 400 or 409)
        {
            var problem = new ProblemDetails
            {
                Status = result.StatusCode,
                Title = "Invoice allocation could not be completed.",
                Detail = result.Error.Message,
                Type = $"urn:iris:invoice-allocation:{code}",
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = code;
            problem.Extensions["receiptId"] = id;
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return new ObjectResult(problem) { StatusCode = result.StatusCode, ContentTypes = { "application/problem+json" } };
        }
        return FromServiceResult(result);
    }

    [HttpPost("bank-receipts/{id:int}/invoice-allocations/avoir-refund")]
    public async Task<IActionResult> RefundAvoir(int id, [FromBody] AvoirRefundRequestDto request, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.RefundAvoirAsync(id, request, cancellationToken));

    [HttpGet("invoices/{avoirInvoiceId:int}/eligible-refund-receipts")]
    public async Task<IActionResult> EligibleAvoirRefundReceipts(int avoirInvoiceId, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.GetEligibleAvoirRefundReceiptsAsync(avoirInvoiceId, cancellationToken));

    [HttpGet("remittances")]
    public async Task<IActionResult> Remittances([FromQuery] RemittanceQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.ListRemittancesAsync(query, cancellationToken));

    [HttpPost("bank-receipts/{id:int}/remittances/generate")]
    public async Task<IActionResult> GenerateRemittances(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.GenerateReceiptRemittancesAsync(id, cancellationToken));

    [HttpPost("remittances/repair-draft-avoir-aggregates")]
    public async Task<IActionResult> RepairDraftAvoirAggregates(CancellationToken cancellationToken) =>
        FromServiceResult(await _service.RepairDraftAvoirRemittancesAsync(cancellationToken));

    [HttpPost("remittances/closings")]
    public async Task<IActionResult> PrintClosing([FromBody] OutgoingClosingRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateOutgoingClosingAsync(request, cancellationToken);
        if (!result.Succeeded) return FromServiceResult(result);
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpGet("remittances/closings/{closingId:int}/document")]
    public async Task<IActionResult> ClosingDocument(int closingId, CancellationToken cancellationToken)
    {
        var result = await _service.GetOutgoingClosingDocumentAsync(closingId, cancellationToken);
        if (!result.Succeeded) return FromServiceResult(result);
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("remittances/{remittanceRef:int}/confirm")]
    public async Task<IActionResult> ConfirmRemittance(int remittanceRef, CancellationToken cancellationToken) => FromServiceResult(await _service.ConfirmRemittanceAsync(remittanceRef, cancellationToken));

    [HttpPost("remittances/{remittanceRef:int}/confirm-for-disbursement")]
    public async Task<IActionResult> ConfirmRemittanceForDisbursement(int remittanceRef, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ConfirmRemittanceForDisbursementAsync(remittanceRef, cancellationToken));

    [HttpPost("remittances/confirm-for-disbursement/batch")]
    public async Task<IActionResult> ConfirmRemittancesForDisbursement([FromBody] RemittanceBatchConfirmationRequestDto request, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ConfirmRemittancesForDisbursementAsync(request, cancellationToken));

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices([FromQuery] InvoiceQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.ListInvoicesAsync(query, cancellationToken));

    [HttpPost("invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] InvoiceRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateInvoiceAsync(request, cancellationToken));

    [HttpGet("invoices/{id:int}")]
    public async Task<IActionResult> Invoice(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.GetInvoiceAsync(id, cancellationToken));

    [HttpGet("contracts/{contractId:int}/invoices")]
    public async Task<IActionResult> ContractInvoices(int contractId, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ListContractInvoicesAsync(contractId, cancellationToken));

    [HttpGet("contracts/{contractId:int}/financial-note/pdf")]
    [HttpGet("contracts/{contractId:int}/debit-note")]
    public async Task<IActionResult> ContractFinancialNote(int contractId, CancellationToken cancellationToken)
    {
        var result = await _service.GetContractFinancialNoteAsync(contractId, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Error?.Code == "IRIS_BANK_ACCOUNT_NOT_CONFIGURED")
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "IrisBankAccountConfigurationInvalid",
                    message = "The IRIS bank accounts required for the financial note are not completely configured.",
                    missingFields = result.Error.Errors,
                    traceId = result.Error.TraceId ?? HttpContext.TraceIdentifier
                });
            if (result.Error is not null && string.IsNullOrWhiteSpace(result.Error.TraceId))
                result.Error.TraceId = HttpContext.TraceIdentifier;
            return FromServiceResult(result);
        }
        SetFinancialPdfHeaders("debit-note-v2");
        Response.Headers["X-Financial-Note-Type"] = result.Value!.DocumentType ?? string.Empty;
        Response.Headers["X-Financial-Note-Reference"] = result.Value.DocumentReference ?? string.Empty;
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("contracts/{contractId:int}/invoices/preview")]
    public async Task<IActionResult> PreviewPremiumInvoice(int contractId, [FromBody] PremiumDebitNoteRequestDto request, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.PreviewPremiumInvoiceAsync(contractId, request, cancellationToken));

    [HttpPost("contracts/{contractId:int}/invoices")]
    public async Task<IActionResult> CreatePremiumInvoice(int contractId, [FromBody] PremiumDebitNoteRequestDto request, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.CreatePremiumInvoiceAsync(contractId, request, cancellationToken));

    [HttpGet("invoices/{invoiceId:int}/document")]
    [HttpGet("invoices/{invoiceId:int}/pdf")]
    public async Task<IActionResult> PremiumInvoiceDocument(int invoiceId, CancellationToken cancellationToken)
    {
        var result = await _service.GetPremiumInvoiceDocumentAsync(invoiceId, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Error?.Code == "INVOICE_NOT_FOUND")
                return NotFound(new { error = "InvoiceNotFound", invoiceId, traceId = HttpContext.TraceIdentifier });
            if (result.Error?.Code == "INVOICE_FINANCIAL_TOTALS_INCONSISTENT")
                return Conflict(new { error = result.Error.Code, message = result.Error.Message, traceId = HttpContext.TraceIdentifier });
            return FromServiceResult(result);
        }
        SetFinancialPdfHeaders("iris-accounting-v1");
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpGet("avoirs/{avoirId:int}/credit-note/pdf")]
    [HttpGet("avoirs/{avoirId:int}/pdf")]
    public async Task<IActionResult> AvoirCreditNoteDocument(int avoirId, CancellationToken cancellationToken)
    {
        var result = await _service.GetAvoirCreditNoteDocumentAsync(avoirId, cancellationToken);
        if (!result.Succeeded) return FromServiceResult(result);
        SetFinancialPdfHeaders("iris-accounting-v1");
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    private void SetFinancialPdfHeaders(string templateVersion)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-PDF-Template-Version"] = templateVersion;
    }
}
