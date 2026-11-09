using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize(Policy = "Module:CounterpartyBankAccounts")]
[Route("api/counterparties/{counterpartyId:int}/bank-accounts")]
public sealed class FinancialAccountsController : ApiControllerBase
{
    private readonly IFinancialWorkflowService _service;
    public FinancialAccountsController(IFinancialWorkflowService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(int counterpartyId, [FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default) => FromServiceResult(await _service.GetBankAccountsAsync(counterpartyId, activeOnly, cancellationToken));

    [HttpGet("{bankAccountId:int}")]
    public async Task<IActionResult> Detail(int counterpartyId, int bankAccountId, CancellationToken cancellationToken) => FromServiceResult(await _service.GetBankAccountAsync(counterpartyId, bankAccountId, cancellationToken));

    [HttpPatch("{bankAccountId:int}/default")]
    public async Task<IActionResult> Default(int counterpartyId, int bankAccountId, CancellationToken cancellationToken) => FromServiceResult(await _service.SetBankAccountDefaultAsync(counterpartyId, bankAccountId, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(int counterpartyId, [FromBody] BankAccountRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.CreateBankAccountAsync(counterpartyId, request, cancellationToken));

    [HttpPut("{bankAccountId:int}")]
    public async Task<IActionResult> Update(int counterpartyId, int bankAccountId, [FromBody] BankAccountRequestDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.UpdateBankAccountAsync(counterpartyId, bankAccountId, request, cancellationToken));

    [HttpPatch("{bankAccountId:int}/status")]
    public async Task<IActionResult> Status(int counterpartyId, int bankAccountId, [FromBody] CounterpartyStatusUpdateDto request, CancellationToken cancellationToken) => FromServiceResult(await _service.SetBankAccountStatusAsync(counterpartyId, bankAccountId, request.IsActive, cancellationToken));
}
