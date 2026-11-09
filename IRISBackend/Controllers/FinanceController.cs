using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/finance")]
public sealed class FinanceController : ApiControllerBase
{
    private readonly IFinancialWorkflowService _service;
    public FinanceController(IFinancialWorkflowService service) => _service = service;

    [HttpGet("bank-account-options")]
    [Authorize(Policy = "Module:FinancialAccountAccess")]
    public async Task<IActionResult> BankAccountOptions([FromQuery] BankAccountOptionQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.GetBankAccountOptionsAsync(query, cancellationToken));

    [HttpGet("bank-receipts")]
    [Authorize(Policy = "Module:Encaissement")]
    public async Task<IActionResult> Receipts([FromQuery] FinancialReceiptQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.ListReceiptsAsync(query, false, cancellationToken));

    [HttpGet("bank-receipts/{id:int}")]
    [Authorize(Policy = "Module:Encaissement")]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.GetReceiptAsync(id, cancellationToken));

    [HttpPost("bank-receipts")]
    [Authorize(Policy = "Module:Encaissement")]
    public async Task<IActionResult> CreateReceipt([FromBody] BankReceiptRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateReceiptAsync(request, cancellationToken));

    [HttpGet("disbursement-queue")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> DisbursementQueue(CancellationToken cancellationToken) => FromServiceResult(await _service.ListDisbursementQueueAsync(cancellationToken));

    [HttpGet("disbursements/pending")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> PendingDisbursements(CancellationToken cancellationToken) => FromServiceResult(await _service.ListDisbursementQueueAsync(cancellationToken));

    [HttpPost("disbursements")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> CreateDisbursement([FromBody] DisbursementRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateDisbursementAsync(request, cancellationToken));

    [HttpGet("remittances/{id:int}/disbursement-context")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> DisbursementContext(int id, CancellationToken cancellationToken, [FromQuery] int? bankAccountId = null) =>
        FromServiceResult(await _service.GetDisbursementContextAsync(id, cancellationToken, bankAccountId));

    [HttpGet("remittances/{id:int}/credit-note")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> AvoirRefundCreditNote(int id, CancellationToken cancellationToken)
    {
        var result = await _service.GetAvoirRefundCreditNoteAsync(id, cancellationToken);
        return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : FromServiceResult(result);
    }

    [HttpGet("closings/{closingId:int}/disbursement-context")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> ClosingDisbursementContext(int closingId, CancellationToken cancellationToken, [FromQuery] int? bankAccountId = null) =>
        FromServiceResult(await _service.GetClosingDisbursementContextAsync(closingId, cancellationToken, bankAccountId));

    [HttpGet("closings/{closingId:int}/pdf")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> ClosingPdf(int closingId, CancellationToken cancellationToken)
    {
        var result = await _service.GetFinanceClosingPdfAsync(closingId, cancellationToken);
        return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : FromServiceResult(result);
    }

    [HttpPost("closings/{closingId:int}/start-disbursement")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> StartClosingDisbursement(int closingId, CancellationToken cancellationToken, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StartPaymentRequestDto? request = null) =>
        FromServiceResult(await _service.StartClosingDisbursementAsync(closingId, cancellationToken, request?.BankAccountId));

    [HttpPost("closings/{closingId:int}/confirm-transfer")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> ConfirmClosingTransfer(int closingId, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ConfirmClosingTransferAsync(closingId, cancellationToken));

    [HttpPost("remittances/{id:int}/start-disbursement")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> StartDisbursement(int id, CancellationToken cancellationToken, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] StartPaymentRequestDto? request = null) =>
        FromServiceResult(await _service.StartDisbursementAsync(id, cancellationToken, request?.BankAccountId));

    [HttpPost("disbursements/{id:int}/confirm")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> ConfirmDisbursement(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.ConfirmDisbursementAsync(id, cancellationToken));

    [HttpPost("disbursements/{id:int}/confirm-transfer")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> ConfirmTransfer(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ConfirmTransferAsync(id, cancellationToken));

    [HttpPost("remittances/{id:int}/disburse")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> DisburseRemittance(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.StartDisbursementAsync(id, cancellationToken));

    [HttpGet("disbursements/{id:int}/invoice")]
    [Authorize(Policy = "Module:Decaissement")]
    public async Task<IActionResult> DisbursementInvoice(int id, CancellationToken cancellationToken)
    {
        var result = await _service.GetDisbursementInvoiceAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return FromServiceResult(result);
        }

        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
}
