using IRISBackend.DTOs;

namespace IRISBackend.Services;

public interface IFinancialWorkflowService
{
    Task<ServiceResult<IReadOnlyList<BankAccountResponseDto>>> GetBankAccountsAsync(int counterpartyId, bool activeOnly, CancellationToken cancellationToken);
    Task<ServiceResult<PagedResponseDto<BankAccountOptionDto>>> GetBankAccountOptionsAsync(BankAccountOptionQueryDto query, CancellationToken cancellationToken);
    Task<ServiceResult<BankAccountResponseDto>> CreateBankAccountAsync(int counterpartyId, BankAccountRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<BankAccountResponseDto>> UpdateBankAccountAsync(int counterpartyId, int bankAccountId, BankAccountRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> SetBankAccountStatusAsync(int counterpartyId, int bankAccountId, bool isActive, CancellationToken cancellationToken);
    Task<ServiceResult<PagedResponseDto<BankReceiptResponseDto>>> ListReceiptsAsync(FinancialReceiptQueryDto query, bool backOffice, CancellationToken cancellationToken);
    Task<ServiceResult<BankReceiptResponseDto>> GetReceiptAsync(int id, CancellationToken cancellationToken);
    Task<ServiceResult<BankReceiptResponseDto>> CreateReceiptAsync(BankReceiptRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<EligibleContractDto>>> GetEligibleContractsAsync(int receiptId, string? search, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<EligibleInvoiceDto>>> GetEligibleInvoicesAsync(int receiptId, string? search, CancellationToken cancellationToken);
    Task<ServiceResult<ReceiptBatchAllocationResponseDto>> AllocateReceiptInvoicesAsync(int receiptId, InvoiceReceiptAllocationRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<BankReceiptResponseDto>> AllocateReceiptAsync(int receiptId, ReceiptAllocationRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<ReceiptBatchAllocationResponseDto>> AllocateReceiptBatchAsync(int receiptId, ReceiptAllocationRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> ListRemittancesAsync(CancellationToken cancellationToken);
    Task<ServiceResult<PagedResponseDto<RemittanceResponseDto>>> ListRemittancesAsync(RemittanceQueryDto query, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> GenerateReceiptRemittancesAsync(int receiptId, CancellationToken cancellationToken);
    Task<ServiceResult<PrintablePaymentDocumentDto>> CreateOutgoingClosingAsync(OutgoingClosingRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<PrintablePaymentDocumentDto>> GetOutgoingClosingDocumentAsync(int closingId, CancellationToken cancellationToken);
    Task<ServiceResult<RemittanceResponseDto>> ConfirmRemittanceAsync(int remittanceRef, CancellationToken cancellationToken);
    Task<ServiceResult<RemittanceConfirmationResponseDto>> ConfirmRemittanceForDisbursementAsync(int remittanceRef, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<NotificationResponseDto>>> GetNotificationsAsync(bool unreadOnly, CancellationToken cancellationToken);
    Task<ServiceResult<int>> GetUnreadNotificationCountAsync(CancellationToken cancellationToken);
    Task<ServiceResult<bool>> MarkNotificationReadAsync(int id, bool all, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> ListDisbursementQueueAsync(CancellationToken cancellationToken);
    Task<ServiceResult<DisbursementResponseDto>> CreateDisbursementAsync(DisbursementRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<DisbursementContextDto>> GetDisbursementContextAsync(int remittanceRef, CancellationToken cancellationToken);
    Task<ServiceResult<ClosingDisbursementContextDto>> GetClosingDisbursementContextAsync(int closingId, CancellationToken cancellationToken);
    Task<ServiceResult<PrintablePaymentDocumentDto>> GetFinanceClosingPdfAsync(int closingId, CancellationToken cancellationToken);
    Task<ServiceResult<ClosingDisbursementContextDto>> StartClosingDisbursementAsync(int closingId, CancellationToken cancellationToken);
    Task<ServiceResult<ClosingDisbursementContextDto>> ConfirmClosingTransferAsync(int closingId, CancellationToken cancellationToken);
    Task<ServiceResult<StartDisbursementResponseDto>> StartDisbursementAsync(int remittanceRef, CancellationToken cancellationToken);
    Task<ServiceResult<DisbursementResponseDto>> ConfirmDisbursementAsync(int id, CancellationToken cancellationToken);
    Task<ServiceResult<ConfirmTransferResponseDto>> ConfirmTransferAsync(int id, CancellationToken cancellationToken);
    Task<ServiceResult<PrintablePaymentDocumentDto>> GetDisbursementInvoiceAsync(int disbursementId, CancellationToken cancellationToken);
    Task<ServiceResult<PagedResponseDto<InvoiceResponseDto>>> ListInvoicesAsync(InvoiceQueryDto query, CancellationToken cancellationToken);
    Task<ServiceResult<InvoiceResponseDto>> CreateInvoiceAsync(InvoiceRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<InvoiceContextDto>> GetInvoiceContextAsync(int contractId, CancellationToken cancellationToken);
    Task<ServiceResult<PremiumDebitNotePreviewDto>> PreviewPremiumInvoiceAsync(int contractId, PremiumDebitNoteRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<PremiumDebitNoteResponseDto>> CreatePremiumInvoiceAsync(int contractId, PremiumDebitNoteRequestDto request, CancellationToken cancellationToken);
    Task<ServiceResult<PrintablePaymentDocumentDto>> GetContractDebitNoteAsync(int contractId, CancellationToken cancellationToken);
    Task<ServiceResult<IReadOnlyList<PremiumDebitNoteResponseDto>>> ListContractInvoicesAsync(int contractId, CancellationToken cancellationToken);
    Task<ServiceResult<PrintablePaymentDocumentDto>> GetPremiumInvoiceDocumentAsync(int invoiceId, CancellationToken cancellationToken);
    Task<ServiceResult<InvoiceResponseDto>> GetInvoiceAsync(int id, CancellationToken cancellationToken);
    Task<ServiceResult<FinancialHistoryDto>> GetFinancialHistoryAsync(int contractId, CancellationToken cancellationToken);
    Task<ServiceResult<CedentClosingResponseDto>> CreateCedentClosingAsync(int contractId, CedentClosingRequestDto request, CancellationToken cancellationToken);
}
