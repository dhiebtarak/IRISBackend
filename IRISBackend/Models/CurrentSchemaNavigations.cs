using System;
using System.Collections.Generic;

namespace IRISBackend.Models;

// Application additions to the reverse-engineered model. Keeping these in a
// partial file prevents a future Power Tools refresh from removing them.
public partial class Counterparty
{
    public virtual ICollection<CounterpartyTypeAssignment> TypeAssignments { get; set; } = new List<CounterpartyTypeAssignment>();
    public virtual ICollection<CounterpartyBankAccount> CounterpartyBankAccounts { get; set; } = new List<CounterpartyBankAccount>();
    public virtual ICollection<BankReceipt> BankReceipts { get; set; } = new List<BankReceipt>();
    public virtual ICollection<CollectionNote> CollectionNotes { get; set; } = new List<CollectionNote>();
    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public virtual ICollection<ContractPanel> ContractPanels { get; set; } = new List<ContractPanel>();
    public virtual ICollection<DocumentAttachment> DocumentAttachments { get; set; } = new List<DocumentAttachment>();
    public virtual ICollection<Disbursement> Disbursements { get; set; } = new List<Disbursement>();
    public virtual ICollection<PremiumRef> PremiumRefs { get; set; } = new List<PremiumRef>();
    public virtual ICollection<RemittanceRef> RemittanceRefs { get; set; } = new List<RemittanceRef>();
}

public partial class AspNetUser
{
    public virtual ICollection<BankReceipt> BankReceiptCreatedByNavigations { get; set; } = new List<BankReceipt>();
    public virtual ICollection<BankReceipt> BankReceiptUpdatedByNavigations { get; set; } = new List<BankReceipt>();
    public virtual ICollection<Claim> ClaimCreatedByNavigations { get; set; } = new List<Claim>();
    public virtual ICollection<Claim> ClaimIdUserNavigations { get; set; } = new List<Claim>();
    public virtual ICollection<Claim> ClaimUpdatedByNavigations { get; set; } = new List<Claim>();
    public virtual ICollection<CollectionNote> CollectionNoteCreatedByNavigations { get; set; } = new List<CollectionNote>();
    public virtual ICollection<CollectionNote> CollectionNoteUpdatedByNavigations { get; set; } = new List<CollectionNote>();
    public virtual ICollection<Contract> ContractCreatedByNavigations { get; set; } = new List<Contract>();
    public virtual ICollection<Contract> ContractIdUserNavigations { get; set; } = new List<Contract>();
    public virtual ICollection<Contract> ContractUpdatedByNavigations { get; set; } = new List<Contract>();
    public virtual ICollection<ContractCoverageSelection> ContractCoverageSelectionCreatedByNavigations { get; set; } = new List<ContractCoverageSelection>();
    public virtual ICollection<ContractCoverageSelection> ContractCoverageSelectionUpdatedByNavigations { get; set; } = new List<ContractCoverageSelection>();
    public virtual ICollection<CounterpartyBankAccount> CounterpartyBankAccountCreatedByNavigations { get; set; } = new List<CounterpartyBankAccount>();
    public virtual ICollection<CounterpartyBankAccount> CounterpartyBankAccountUpdatedByNavigations { get; set; } = new List<CounterpartyBankAccount>();
    public virtual ICollection<CoverageItem> CoverageItemCreatedByNavigations { get; set; } = new List<CoverageItem>();
    public virtual ICollection<CoverageItem> CoverageItemUpdatedByNavigations { get; set; } = new List<CoverageItem>();
    public virtual ICollection<CoverageRelation> CoverageRelationCreatedByNavigations { get; set; } = new List<CoverageRelation>();
    public virtual ICollection<CoverageRelation> CoverageRelationUpdatedByNavigations { get; set; } = new List<CoverageRelation>();
    public virtual ICollection<Disbursement> DisbursementCreatedByNavigations { get; set; } = new List<Disbursement>();
    public virtual ICollection<Disbursement> DisbursementIdUserNavigations { get; set; } = new List<Disbursement>();
    public virtual ICollection<Disbursement> DisbursementUpdatedByNavigations { get; set; } = new List<Disbursement>();
    public virtual ICollection<DocumentAttachment> DocumentAttachmentCreatedByNavigations { get; set; } = new List<DocumentAttachment>();
    public virtual ICollection<DocumentAttachment> DocumentAttachmentUpdatedByNavigations { get; set; } = new List<DocumentAttachment>();
    public virtual ICollection<Endorsement> EndorsementCreatedByNavigations { get; set; } = new List<Endorsement>();
    public virtual ICollection<Endorsement> EndorsementUpdatedByNavigations { get; set; } = new List<Endorsement>();
    public virtual ICollection<Invoice> InvoiceCreatedByNavigations { get; set; } = new List<Invoice>();
    public virtual ICollection<Invoice> InvoiceIdUserNavigations { get; set; } = new List<Invoice>();
    public virtual ICollection<Invoice> InvoiceUpdatedByNavigations { get; set; } = new List<Invoice>();
    public virtual ICollection<PremiumRef> PremiumRefCreatedByNavigations { get; set; } = new List<PremiumRef>();
    public virtual ICollection<PremiumRef> PremiumRefIdUserNavigations { get; set; } = new List<PremiumRef>();
    public virtual ICollection<PremiumRef> PremiumRefUpdatedByNavigations { get; set; } = new List<PremiumRef>();
    public virtual ICollection<ReceiptAllocation> ReceiptAllocations { get; set; } = new List<ReceiptAllocation>();
    public virtual ICollection<RemittanceRef> RemittanceRefCreatedByNavigations { get; set; } = new List<RemittanceRef>();
    public virtual ICollection<RemittanceRef> RemittanceRefUpdatedByNavigations { get; set; } = new List<RemittanceRef>();
    public virtual ICollection<SlipTemplate> SlipTemplates { get; set; } = new List<SlipTemplate>();
}

public partial class CounterpartyBankAccount
{
    public string? BankCity { get; set; }
    public string? BankCountry { get; set; }
    public bool HasCorrespondentBank { get; set; }
    public string? CorrespondentBankName { get; set; }
    public string? CorrespondentSwiftBic { get; set; }
}

public partial class BankReceipt
{
    public string? BusinessReference { get; set; }
    public string? DeclaredInvoiceNumber { get; set; }
    public DateOnly? DeclaredInvoiceMonth { get; set; }
}

public partial class ReceiptAllocation
{
    public int? IdContract { get; set; }
    public int? IdPanel { get; set; }
    public int? IdInvoice { get; set; }
    public virtual Contract? IdContractNavigation { get; set; }
    public virtual ContractPanel? IdPanelNavigation { get; set; }
    public virtual Invoice? IdInvoiceNavigation { get; set; }
}

public partial class Contract
{
    public virtual ICollection<ReceiptAllocation> ReceiptAllocations { get; set; } = new List<ReceiptAllocation>();
}

public partial class RemittanceRef
{
    public int? IdReceiptAllocation { get; set; }
    public virtual ReceiptAllocation? IdReceiptAllocationNavigation { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
}

public partial class Invoice
{
    public int? IdReceiptAllocation { get; set; }
    public int? IdCurrency { get; set; }
    public int? IdCounterparty { get; set; }
    public int? IdPanel { get; set; }
    public int? RemittanceRef { get; set; }
    public int? IdDisbursement { get; set; }
    public virtual Counterparty? IdCounterpartyNavigation { get; set; }
    public virtual Currency? IdCurrencyNavigation { get; set; }
    public virtual ContractPanel? IdPanelNavigation { get; set; }
    public virtual ReceiptAllocation? IdReceiptAllocationNavigation { get; set; }
    public virtual RemittanceRef? RemittanceRefNavigation { get; set; }
    public virtual Disbursement? IdDisbursementNavigation { get; set; }
    public virtual ICollection<ReceiptAllocation> ReceiptAllocations { get; set; } = new List<ReceiptAllocation>();
}

public partial class Disbursement
{
    public string? BankReference { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? StartedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public string? BankNameSnapshot { get; set; }
    public string? BankCitySnapshot { get; set; }
    public string? BankCountrySnapshot { get; set; }
    public string? IbanSnapshot { get; set; }
    public string? SwiftBicSnapshot { get; set; }
    public string? CorrespondentBankNameSnapshot { get; set; }
    public string? CorrespondentSwiftBicSnapshot { get; set; }
    public decimal? GrossPremiumSnapshot { get; set; }
    public decimal? DeductionsSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public string? ContractNameSnapshot { get; set; }
    public string? UmrSnapshot { get; set; }
    public string? CedentNameSnapshot { get; set; }
    public string? ReinsurerNameSnapshot { get; set; }
    public string? ReinsurerAddressSnapshot { get; set; }
    public string? ReinsurerCitySnapshot { get; set; }
    public string? ReinsurerCountrySnapshot { get; set; }
    public string? ReinsurerTaxIdSnapshot { get; set; }
    public string? PanelReferenceSnapshot { get; set; }
}
