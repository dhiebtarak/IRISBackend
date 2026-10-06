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
    public async Task<IActionResult> BankAccountOptions([FromQuery] BankAccountOptionQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.GetBankAccountOptionsAsync(query, cancellationToken));

    [HttpGet("bank-receipts")]
    public async Task<IActionResult> Receipts([FromQuery] FinancialReceiptQueryDto query, CancellationToken cancellationToken) => FromServiceResult(await _service.ListReceiptsAsync(query, false, cancellationToken));

    [HttpGet("bank-receipts/{id:int}")]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.GetReceiptAsync(id, cancellationToken));

    [HttpPost("bank-receipts")]
    public async Task<IActionResult> CreateReceipt([FromBody] BankReceiptRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateReceiptAsync(request, cancellationToken));

    [HttpGet("disbursement-queue")]
    public async Task<IActionResult> DisbursementQueue(CancellationToken cancellationToken) => FromServiceResult(await _service.ListDisbursementQueueAsync(cancellationToken));

    [HttpGet("disbursements/pending")]
    public async Task<IActionResult> PendingDisbursements(CancellationToken cancellationToken) => FromServiceResult(await _service.ListDisbursementQueueAsync(cancellationToken));

    [HttpPost("disbursements")]
    public async Task<IActionResult> CreateDisbursement([FromBody] DisbursementRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateDisbursementAsync(request, cancellationToken));

    [HttpGet("remittances/{id:int}/disbursement-context")]
    public async Task<IActionResult> DisbursementContext(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.GetDisbursementContextAsync(id, cancellationToken));

    [HttpPost("remittances/{id:int}/start-disbursement")]
    public async Task<IActionResult> StartDisbursement(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.StartDisbursementAsync(id, cancellationToken));

    [HttpPost("disbursements/{id:int}/confirm")]
    public async Task<IActionResult> ConfirmDisbursement(int id, CancellationToken cancellationToken) => FromServiceResult(await _service.ConfirmDisbursementAsync(id, cancellationToken));

    [HttpPost("disbursements/{id:int}/confirm-transfer")]
    public async Task<IActionResult> ConfirmTransfer(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.ConfirmTransferAsync(id, cancellationToken));

    [HttpPost("remittances/{id:int}/disburse")]
    public async Task<IActionResult> DisburseRemittance(int id, CancellationToken cancellationToken) =>
        FromServiceResult(await _service.StartDisbursementAsync(id, cancellationToken));

    [HttpGet("disbursements/{id:int}/invoice")]
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
