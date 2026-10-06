using System.Text.Json.Serialization;

namespace IRISBackend.DTOs;

public sealed class BankAccountRequestDto
{
    public int? CounterpartyId { get; set; }
    public int CurrencyId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string BankCity { get; set; } = string.Empty;
    public string BankCountry { get; set; } = string.Empty;
    public string? AgencyCode { get; set; }
    public string? Rib { get; set; }
    public string? Iban { get; set; }
    public string? SwiftBic { get; set; }
    public bool HasCorrespondentBank { get; set; }
    public string? CorrespondentBankName { get; set; }
    public string? CorrespondentSwiftBic { get; set; }
    public string? AccountLabel { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BankAccountResponseDto
{
    public int IdBankAccount { get; set; }
    public int IdCounterparty { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public string? BeneficiaryAddress { get; set; }
    public string? BeneficiaryCity { get; set; }
    public string? BeneficiaryCountry { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string? BankCity { get; set; }
    public string? BankCountry { get; set; }
    public string? AgencyCode { get; set; }
    public string? Rib { get; set; }
    [JsonIgnore]
    public string? Iban { get; set; }
    public string? MaskedIban { get; set; }
    public string? SwiftBic { get; set; }
    public bool HasCorrespondentBank { get; set; }
    public string? CorrespondentBankName { get; set; }
    public string? CorrespondentSwiftBic { get; set; }
    public string? AccountLabel { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

public sealed class BankReceiptRequestDto
{
    public int BankAccountId { get; set; }
    public string ReceiptMonth { get; set; } = string.Empty;
    public decimal AmountReceived { get; set; }
    public decimal? AppliedExchangeRate { get; set; }
    public string SettlementMethod { get; set; } = string.Empty;
    public string CollectionType { get; set; } = string.Empty;
    public string BankReference { get; set; } = string.Empty;
    public string SettlementLabel { get; set; } = string.Empty;
    public string? BusinessReference { get; set; }
    public string? DeclaredInvoiceNumber { get; set; }
    public string? DeclaredInvoiceMonth { get; set; }
    public string? Reason { get; set; }
}

public sealed class BankAccountOptionQueryDto
{
    public string? Search { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class BankAccountOptionDto
{
    public int IdBankAccount { get; set; }
    public int CounterpartyId { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public string? BankCity { get; set; }
    public string? BankCountry { get; set; }
    public string? MaskedIban { get; set; }
    public string? SwiftBic { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class FinancialReceiptQueryDto
{
    public string? Search { get; set; }
    public int? CounterpartyId { get; set; }
    public int? CurrencyId { get; set; }
    public string? PairingStatus { get; set; }
    public DateOnly? OperationFrom { get; set; }
    public DateOnly? OperationTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}

public sealed class BankReceiptResponseDto
{
    public int IdBankReceipt { get; set; }
    public int BankAccountId { get; set; }
    public int CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string? MaskedIban { get; set; }
    public DateOnly ReceptionDate { get; set; }
    public DateOnly OperationDate { get; set; }
    public string ReceiptMonth { get; set; } = string.Empty;
    public decimal AmountReceived { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? AppliedExchangeRate { get; set; }
    public decimal AvailableAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public string PairingStatus { get; set; } = string.Empty;
    public string SettlementMethod { get; set; } = string.Empty;
    public string CollectionType { get; set; } = string.Empty;
    public string BankReference { get; set; } = string.Empty;
    public string SettlementLabel { get; set; } = string.Empty;
    public string? BusinessReference { get; set; }
    public string? DeclaredInvoiceNumber { get; set; }
    public DateOnly? DeclaredInvoiceMonth { get; set; }
    public string? Reason { get; set; }
}

public sealed class ReceiptAllocationRequestDto
{
    public int ContractId { get; set; }
    public decimal AllocatedAmount { get; set; }
    public List<ReceiptAllocationItemDto>? Allocations { get; set; }
}

public sealed class ReceiptAllocationItemDto
{
    public int ContractId { get; set; }
    public int? InvoiceId { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public sealed class InvoiceReceiptAllocationRequestDto
{
    public List<InvoiceReceiptAllocationItemDto>? Allocations { get; set; }
}

public sealed class InvoiceReceiptAllocationItemDto
{
    public int InvoiceId { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public sealed class ReceiptBatchAllocationResponseDto
{
    public int ReceiptId { get; set; }
    public IReadOnlyList<ReceiptBatchAllocationItemDto> Allocations { get; set; } = [];
    public decimal TotalAllocated { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PairingStatus { get; set; } = string.Empty;
}

public sealed class ReceiptBatchAllocationItemDto
{
    public int IdReceiptAllocation { get; set; }
    public int? ContractId { get; set; }
    public int? InvoiceId { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public sealed class EligibleInvoiceDto
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string? Umr { get; set; }
    public int CedentId { get; set; }
    public string CedentName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal AlreadyPairedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class EligibleContractDto
{
    public int ContractId { get; set; }
    public string? Umr { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public int CedentId { get; set; }
    public string CedentName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public int EligibleReinsurerCount { get; set; }
}

public sealed class RemittanceResponseDto
{
    public int RemittanceRef { get; set; }
    public string RemittanceReference { get; set; } = string.Empty;
    public int? ReceiptAllocationId { get; set; }
    public int? DisbursementId { get; set; }
    public int? InvoiceId { get; set; }
    public string? InvoiceDownloadUrl { get; set; }
    public int? PanelId { get; set; }
    public int CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public decimal? NetPremiumDue { get; set; }
    public decimal? NetPayableAmount { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public int? ContractId { get; set; }
    public string? ContractName { get; set; }
    public string? Umr { get; set; }
    public string? CedentName { get; set; }
    public decimal? GrossPremium { get; set; }
    public decimal? OtherDeductions { get; set; }
    public bool HasActiveBankAccount { get; set; }
    public string? Action { get; set; }
}

public sealed class DisbursementContextDto
{
    public int RemittanceId { get; set; }
    public int? ContractId { get; set; }
    public string? ContractName { get; set; }
    public string? Umr { get; set; }
    public string? CedentName { get; set; }
    public int? PanelId { get; set; }
    public int ReinsurerId { get; set; }
    public string ReinsurerName { get; set; } = string.Empty;
    public decimal? GrossPremium { get; set; }
    public decimal? Deductions { get; set; }
    public decimal NetPayableAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DisbursementId { get; set; }
    public int? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public bool CanDownloadInvoice { get; set; }
    public bool CanStartPayment { get; set; }
    public bool CanConfirmTransfer { get; set; }
    public int ActiveBankAccountCount { get; set; }
    public bool BankAccountSelectionRequired { get; set; }
    public BankAccountSnapshotDto? SelectedBankAccount { get; set; }
    public BankAccountSnapshotDto? DefaultBankAccount { get; set; }
    public string? ValidationMessage { get; set; }
}

public sealed class BankAccountSnapshotDto
{
    public int IdBankAccount { get; set; }
    public string? BankName { get; set; }
    public string? BankCity { get; set; }
    public string? BankCountry { get; set; }
    public string? Iban { get; set; }
    public string? SwiftBic { get; set; }
    public string? CorrespondentBankName { get; set; }
    public string? CorrespondentSwiftBic { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class StartDisbursementResponseDto
{
    public int RemittanceId { get; set; }
    public int DisbursementId { get; set; }
    public int InvoiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string InvoiceDownloadUrl { get; set; } = string.Empty;
    public BankAccountSnapshotDto? SelectedBankAccount { get; set; }
}

public sealed class ConfirmTransferResponseDto
{
    public int DisbursementId { get; set; }
    public int RemittanceId { get; set; }
    public int InvoiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ClosedAt { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }
}

public sealed class RemittanceConfirmationResponseDto
{
    public int RemittanceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool FinanceNotificationCreated { get; set; }
    public int? NotificationId { get; set; }
    public DateTime ConfirmationDate { get; set; }
}

public sealed class InvoiceRequestDto
{
    public int ContractId { get; set; }
    public int? ReceiptAllocationId { get; set; }
    public int? CounterpartyId { get; set; }
    public int? PanelId { get; set; }
    public int? RemittanceRef { get; set; }
    public string InvoiceType { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal? NetPremium { get; set; }
    public decimal? Amount { get; set; }
    public string? Label { get; set; }
    public string? SettlementMethod { get; set; }
}

public sealed class InvoiceQueryDto
{
    public string? Search { get; set; }
    public int? ContractId { get; set; }
    public int? CounterpartyId { get; set; }
    public int? CedentId { get; set; }
    public int? PanelId { get; set; }
    public string? InvoiceType { get; set; }
    public string? PairingStatus { get; set; }
    // String-bound so UI placeholders such as "All currencies" can be ignored
    // instead of causing ASP.NET model binding to reject the entire request.
    public string? CurrencyId { get; set; }
    public string? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}

public sealed class InvoiceResponseDto
{
    public int IdInvoice { get; set; }
    public int InvoiceId { get; set; }
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string? Umr { get; set; }
    public int CedentId { get; set; }
    public int? CounterpartyId { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CedentName { get; set; }
    public int? PanelId { get; set; }
    public int? RemittanceRef { get; set; }
    public int? ReceiptAllocationId { get; set; }
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal? NetPremium { get; set; }
    public decimal? NetAmount { get; set; }
    public string? RecoveryStatus { get; set; }
    public string? PairingStatus { get; set; }
    public decimal PairedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal AlreadyInvoicedAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public decimal AffectedAmount { get; set; }
    public decimal UnaffectedAmount { get; set; }
    public string? ReinsurerName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class InvoiceContextDto
{
    public int ContractId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal ContractGrossPremium { get; set; }
    public decimal? CedentCommissionPct { get; set; }
    public decimal RemainingInvoiceableAmount { get; set; }
    public bool CanCreateInvoice { get; set; }
    public string? BlockingReason { get; set; }
    public decimal PairedAmount { get; set; }
    public decimal AlreadyInvoicedAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public IReadOnlyList<InvoiceAllocationOptionDto> ReceiptAllocations { get; set; } = [];
    public IReadOnlyList<InvoicePanelOptionDto> ReinsurerPanels { get; set; } = [];
    public decimal InvoiceableGrossPremium { get; set; }
    public IReadOnlyList<InvoiceResponseDto> PreviousInvoices { get; set; } = [];
}

public sealed class InvoiceAllocationOptionDto
{
    public int ReceiptAllocationId { get; set; }
    public int BankReceiptId { get; set; }
    public string? ReceiptReference { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal AlreadyInvoicedAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class InvoicePanelOptionDto
{
    public int PanelId { get; set; }
    public int CounterpartyId { get; set; }
    public string ReinsurerName { get; set; } = string.Empty;
    public decimal? SignedSharePct { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class PremiumDebitNoteRequestDto
{
    public decimal GrossAmount { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public string? Label { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class PremiumDebitNotePreviewDto
{
    public int ContractId { get; set; }
    public decimal ContractGrossPremium { get; set; }
    public decimal InvoiceableGrossPremium { get; set; }
    public decimal AlreadyInvoicedAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public IReadOnlyList<PremiumDebitNoteAllocationDto> Allocations { get; set; } = [];
}

public sealed class PremiumDebitNoteAllocationDto
{
    public int PanelId { get; set; }
    public string? ReinsurerCode { get; set; }
    public string ReinsurerName { get; set; } = string.Empty;
    public decimal? SignedSharePct { get; set; }
    public decimal AllocationRatio { get; set; }
    public decimal GrossPremium { get; set; }
    public decimal? CedentCommissionPct { get; set; }
    public decimal CedentCommissionAmount { get; set; }
    public decimal BrokeragePct { get; set; }
    public decimal BrokerageAmount { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class PremiumDebitNoteResponseDto
{
    public int InvoiceId { get; set; }
    public int ContractId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int? Sequence { get; set; }
    public string InvoiceType { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal ContractGrossPremium { get; set; }
    public decimal InvoiceableGrossPremium { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal AlreadyInvoicedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal TotalNetAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public IReadOnlyList<PremiumDebitNoteAllocationDto> Allocations { get; set; } = [];
}

public sealed class NotificationResponseDto
{
    public int IdNotification { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? RecipientRole { get; set; }
    public string? TargetRoute { get; set; }
    public IReadOnlyList<NotificationActionDto> Actions { get; set; } = [];
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class NotificationActionDto
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsDownload { get; set; }
}

public sealed class DisbursementRequestDto
{
    public int RemittanceRef { get; set; }
    public int BankAccountId { get; set; }
    public DateOnly OperationDate { get; set; }
    public DateOnly? TransferDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? BankReference { get; set; }
    public string? Description { get; set; }
}

public sealed class DisbursementResponseDto
{
    public int IdDisbursement { get; set; }
    public int RemittanceRef { get; set; }
    public int CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly OperationDate { get; set; }
    public string SignatureStatus { get; set; } = string.Empty;
}

public sealed class FinancialHistoryDto
{
    public IReadOnlyList<BankReceiptResponseDto> Receipts { get; set; } = [];
    public IReadOnlyList<RemittanceResponseDto> Remittances { get; set; } = [];
    public IReadOnlyList<InvoiceResponseDto> Invoices { get; set; } = [];
    public IReadOnlyList<DisbursementResponseDto> Disbursements { get; set; } = [];
}

public sealed class PrintablePaymentDocumentDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = "application/pdf";
}

public sealed class CedentClosingRequestDto
{
    public int CounterpartyId { get; set; }
    public int CurrencyId { get; set; }
    public string? ClosingInformation { get; set; }
    public DateOnly? OperationDate { get; set; }
}

public sealed class CedentClosingResponseDto
{
    public int PremiumRef { get; set; }
    public int ContractId { get; set; }
    public int CounterpartyId { get; set; }
    public int CurrencyId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ClosingInformation { get; set; } = string.Empty;
    public DateOnly? OperationDate { get; set; }
}
