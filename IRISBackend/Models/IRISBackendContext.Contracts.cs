using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    public virtual DbSet<IrisBankAccount> IrisBankAccounts { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IrisBankAccount>(entity =>
        {
            entity.ToTable("IRIS_BANK_ACCOUNT");
            entity.HasKey(account => account.IdIrisBankAccount);
            entity.Property(account => account.IdIrisBankAccount).HasColumnName("id_iris_bank_account");
            entity.Property(account => account.BankName).HasMaxLength(255).IsRequired().HasColumnName("bank_name");
            entity.Property(account => account.AccountHolder).HasMaxLength(255).HasColumnName("account_holder");
            entity.Property(account => account.IdCurrency).IsRequired().HasColumnName("id_currency");
            entity.Property(account => account.Rib).HasMaxLength(64).HasColumnName("rib");
            entity.Property(account => account.Iban).HasMaxLength(34).IsRequired().HasColumnName("iban");
            entity.Property(account => account.BicSwift).HasMaxLength(11).IsRequired().HasColumnName("bic_swift");
            entity.Property(account => account.Domiciliation).HasMaxLength(255).HasColumnName("domiciliation");
            entity.Property(account => account.DisplayOrder).HasDefaultValue(0).HasColumnName("display_order");
            entity.Property(account => account.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(account => account.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").HasColumnName("created_at");
            entity.Property(account => account.CreatedBy).HasMaxLength(450).HasColumnName("created_by");
            entity.Property(account => account.UpdatedAt).HasColumnName("updated_at");
            entity.Property(account => account.UpdatedBy).HasMaxLength(450).HasColumnName("updated_by");
            entity.HasIndex(account => account.IsActive).HasDatabaseName("IX_IRIS_BANK_ACCOUNT_is_active");
            entity.HasOne(account => account.IdCurrencyNavigation).WithMany()
                .HasForeignKey(account => account.IdCurrency)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_IRIS_BANK_ACCOUNT_CURRENCY");
        });

        ConfigureOutgoingRemittances(modelBuilder);
        modelBuilder.Entity<Invoice>()
            .Property(invoice => invoice.RecoveryStatus)
            .HasDefaultValue("Unsettled");

        modelBuilder.Entity<InvoiceNumberSequence>(entity =>
        {
            entity.HasKey(item => new { item.InvoiceYear, item.DocumentType });
            entity.ToTable("INVOICE_NUMBER_SEQUENCE");
            entity.Property(item => item.InvoiceYear).ValueGeneratedNever().HasColumnName("invoice_year");
            entity.Property(item => item.DocumentType).HasMaxLength(10).HasDefaultValue("INV").HasColumnName("document_type");
            entity.Property(item => item.LastSequence).HasColumnName("last_sequence");
        });

        modelBuilder.Entity<InvoiceCreditApplication>(entity =>
        {
            entity.ToTable("INVOICE_CREDIT_APPLICATION", table =>
                table.HasCheckConstraint("CK_INVOICE_CREDIT_APPLICATION_amount_positive", "[amount] > (0)"));
            entity.HasKey(item => item.IdInvoiceCreditApplication);
            entity.Property(item => item.IdInvoiceCreditApplication).HasColumnName("id_invoice_credit_application");
            entity.Property(item => item.IdBankReceipt).HasColumnName("id_bank_receipt");
            entity.Property(item => item.TargetInvoiceId).HasColumnName("target_invoice_id");
            entity.Property(item => item.AvoirInvoiceId).HasColumnName("avoir_invoice_id");
            entity.Property(item => item.Amount).HasColumnType("decimal(19,4)").HasColumnName("amount");
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").HasColumnName("created_at");
            entity.Property(item => item.CreatedBy).HasMaxLength(450).HasColumnName("created_by");
            entity.Property(item => item.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(item => item.OperationId).HasColumnName("operation_id");
            entity.HasIndex(item => new { item.OperationId, item.TargetInvoiceId, item.AvoirInvoiceId })
                .IsUnique().HasDatabaseName("UX_INVOICE_CREDIT_APPLICATION_operation_target_avoir");
            entity.HasOne(item => item.IdBankReceiptNavigation).WithMany()
                .HasForeignKey(item => item.IdBankReceipt).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_INVOICE_CREDIT_APPLICATION_bank_receipt");
            entity.HasOne(item => item.TargetInvoiceNavigation).WithMany(invoice => invoice.CreditApplicationsAsTarget)
                .HasForeignKey(item => item.TargetInvoiceId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_INVOICE_CREDIT_APPLICATION_target_invoice");
            entity.HasOne(item => item.AvoirInvoiceNavigation).WithMany(invoice => invoice.CreditApplicationsAsAvoir)
                .HasForeignKey(item => item.AvoirInvoiceId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_INVOICE_CREDIT_APPLICATION_avoir_invoice");
            entity.HasOne(item => item.OperationNavigation).WithMany(operation => operation.CreditApplications)
                .HasForeignKey(item => item.OperationId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_INVOICE_CREDIT_APPLICATION_operation");
        });

        modelBuilder.Entity<AvoirRefundSettlement>(entity =>
        {
            entity.ToTable("AVOIR_REFUND_SETTLEMENT", table =>
                table.HasCheckConstraint("CK_AVOIR_REFUND_SETTLEMENT_amount_positive", "[amount] > (0)"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id_avoir_refund_settlement");
            entity.Property(item => item.AvoirInvoiceId).HasColumnName("avoir_invoice_id");
            entity.Property(item => item.BankReceiptId).HasColumnName("id_bank_receipt");
            entity.Property(item => item.RemittanceId).HasColumnName("id_remittance_ref");
            entity.Property(item => item.Amount).HasColumnType("decimal(19,4)").HasColumnName("amount");
            entity.Property(item => item.IdempotencyKey).HasMaxLength(100).IsRequired().HasColumnName("idempotency_key");
            entity.Property(item => item.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").HasColumnName("created_at");
            entity.Property(item => item.CreatedBy).HasMaxLength(450).IsRequired().HasColumnName("created_by");
            entity.Property(item => item.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.HasIndex(item => item.IdempotencyKey).IsUnique().HasDatabaseName("UX_AVOIR_REFUND_SETTLEMENT_idempotency_key");
            entity.HasIndex(item => new { item.AvoirInvoiceId, item.IsActive }).HasDatabaseName("IX_AVOIR_REFUND_SETTLEMENT_avoir_active");
            entity.HasOne(item => item.AvoirInvoice).WithMany().HasForeignKey(item => item.AvoirInvoiceId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_AVOIR_REFUND_SETTLEMENT_avoir_invoice");
            entity.HasOne(item => item.BankReceipt).WithMany().HasForeignKey(item => item.BankReceiptId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_AVOIR_REFUND_SETTLEMENT_bank_receipt");
            entity.HasOne(item => item.Remittance).WithMany(item => item.AvoirRefundSettlements).HasForeignKey(item => item.RemittanceId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_AVOIR_REFUND_SETTLEMENT_remittance");
            entity.HasOne<AspNetUser>().WithMany().HasForeignKey(item => item.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_AVOIR_REFUND_SETTLEMENT_created_by");
        });

        modelBuilder.Entity<InvoicePairingOperation>()
            .Property(item => item.SignedPairingTotal)
            .HasColumnType("decimal(19,4)")
            .HasDefaultValue(0m)
            .HasColumnName("signed_pairing_total");

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.Property(e => e.Umr)
                .HasMaxLength(100);

            entity.Property(e => e.CedentPremiumReference)
                .HasMaxLength(100)
                .HasColumnName("Cedent_premium_reference");

            entity.HasIndex(e => e.Umr, "UX_CONTRACT_UMR")
                .IsUnique()
                .HasFilter("[Umr] IS NOT NULL");
        });

        modelBuilder.Entity<ContractUmrSequence>(entity =>
        {
            entity.HasKey(e => new { e.InceptionYear, e.ContractTypeCode, e.MainCoverCode });

            entity.ToTable("CONTRACT_UMR_SEQUENCE");

            entity.Property(e => e.InceptionYear)
                .HasColumnName("inception_year");
            entity.Property(e => e.ContractTypeCode)
                .HasMaxLength(10)
                .HasColumnName("contract_type_code");
            entity.Property(e => e.MainCoverCode)
                .HasMaxLength(50)
                .HasColumnName("main_cover_code");
            entity.Property(e => e.CurrentSequence)
                .HasColumnName("current_sequence");
        });

        modelBuilder.Entity<CounterpartyTypeAssignment>(entity =>
        {
            entity.HasKey(e => new { e.CounterpartyId, e.Type });

            entity.ToTable("COUNTERPARTY_TYPE_ASSIGNMENT");

            entity.Property(e => e.CounterpartyId).HasColumnName("CounterpartyId");
            entity.Property(e => e.Type)
                .HasMaxLength(32)
                .HasColumnName("Type");

            entity.HasIndex(e => e.Type, "IX_COUNTERPARTY_TYPE_ASSIGNMENT_Type");

            entity.HasOne(e => e.Counterparty)
                .WithMany(counterparty => counterparty.TypeAssignments)
                .HasForeignKey(e => e.CounterpartyId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_COUNTERPARTY_TYPE_ASSIGNMENT_Counterparty");
        });

        modelBuilder.Entity<ReceiptAllocation>(entity =>
        {
            entity.Property(e => e.IdPanel).HasColumnName("Id_panel");
            entity.Property(e => e.IdContract).HasColumnName("Id_contract");
            entity.Property(e => e.IdInvoice).HasColumnName("id_invoice");
            entity.HasOne(e => e.IdPanelNavigation).WithMany()
                .HasForeignKey(e => e.IdPanel)
                .HasConstraintName("FK_RECEIPT_ALLOCATION_id_panel");
            entity.HasOne(e => e.IdContractNavigation).WithMany(contract => contract.ReceiptAllocations)
                .HasForeignKey(e => e.IdContract)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_RECEIPT_ALLOCATION_id_contract");
            entity.HasOne(e => e.IdInvoiceNavigation).WithMany(invoice => invoice.ReceiptAllocations)
                .HasForeignKey(e => e.IdInvoice)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_RECEIPT_ALLOCATION_id_invoice");
            entity.HasIndex(e => e.IdInvoice, "IX_RECEIPT_ALLOCATION_id_invoice");
            entity.HasIndex(e => new { e.IdBankReceipt, e.IdInvoice }, "UX_RECEIPT_ALLOCATION_receipt_invoice")
                .IsUnique()
                .HasFilter("[id_invoice] IS NOT NULL AND [is_active] = 1");
        });

        modelBuilder.Entity<DocumentAttachment>(entity =>
        {
            entity.Property(e => e.IdBankReceipt).HasColumnName("id_bank_receipt");
            entity.HasIndex(e => e.IdBankReceipt, "IX_DOCUMENT_ATTACHMENT_id_bank_receipt");
            entity.HasOne(e => e.IdBankReceiptNavigation).WithMany(receipt => receipt.DocumentAttachments)
                .HasForeignKey(e => e.IdBankReceipt)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_DOCUMENT_ATTACHMENT_id_bank_receipt");
        });

        modelBuilder.Entity<CounterpartyBankAccount>(entity =>
        {
            entity.Property(e => e.BankCity).HasMaxLength(150).HasColumnName("Bank_city");
            entity.Property(e => e.BankCountry).HasMaxLength(150).HasColumnName("Bank_country");
            entity.Property(e => e.HasCorrespondentBank).HasDefaultValue(false).HasColumnName("Has_correspondent_bank");
            entity.Property(e => e.CorrespondentBankName).HasMaxLength(255).HasColumnName("Correspondent_bank_name");
            entity.Property(e => e.CorrespondentSwiftBic).HasMaxLength(100).HasColumnName("Correspondent_swift_bic");
            entity.HasIndex(e => new { e.IdCounterparty, e.IdCurrency }, "UX_COUNTERPARTY_BANK_ACCOUNT_default_currency")
                .IsUnique()
                .HasFilter("[Is_active]=(1) AND [Is_default]=(1)");
        });

        modelBuilder.Entity<RemittanceRef>(entity =>
        {
            entity.Property(e => e.IdDisbursement).HasColumnName("Id_disbursement");
            entity.Property(e => e.IdReceiptAllocation).HasColumnName("Id_receipt_allocation");
            entity.Property(e => e.ConfirmedAt).HasColumnName("Confirmed_at");
            entity.Property(e => e.ConfirmedBy).HasMaxLength(450).HasColumnName("Confirmed_by");
            entity.Property(e => e.SourceInvoiceId).HasColumnName("source_invoice_id");
            entity.Property(e => e.BeneficiaryType).HasMaxLength(24).HasColumnName("beneficiary_type");
            entity.HasOne(e => e.SourceInvoice).WithMany().HasForeignKey(e => e.SourceInvoiceId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_REMITTANCE_REF_source_invoice");
            entity.HasOne(e => e.IdReceiptAllocationNavigation).WithMany()
                .HasForeignKey(e => e.IdReceiptAllocation)
                .HasConstraintName("FK_REMITTANCE_REF_id_receipt_allocation");
            entity.HasIndex(e => new { e.IdReceiptAllocation, e.SourceInvoiceId, e.IdInvoiceAllocation, e.IdPanel }, "UX_REMITTANCE_REF_source_allocation_panel")
                .IsUnique()
                .HasFilter("[source_invoice_id] IS NOT NULL AND [id_invoice_allocation] IS NOT NULL AND [Id_panel] IS NOT NULL");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.Property(e => e.GrossPremium).HasColumnType("decimal(19,4)").HasColumnName("Gross_premium");
            entity.Property(e => e.SequenceNumber).HasColumnName("Sequence_number");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100).HasColumnName("Idempotency_key");
            entity.Property(e => e.ProcessingMode).HasMaxLength(40).HasColumnName("processing_mode");
            entity.Property(e => e.IdReceiptAllocation).HasColumnName("Id_receipt_allocation");
            entity.Property(e => e.IdCurrency).HasColumnName("Id_currency");
            entity.HasOne(e => e.IdReceiptAllocationNavigation).WithMany()
                .HasForeignKey(e => e.IdReceiptAllocation)
                .HasConstraintName("FK_INVOICE_id_receipt_allocation");
            entity.HasOne(e => e.IdCurrencyNavigation).WithMany()
                .HasForeignKey(e => e.IdCurrency)
                .HasConstraintName("FK_INVOICE_id_currency");
            entity.HasIndex(e => e.IdReceiptAllocation, "IX_INVOICE_id_receipt_allocation");
            entity.HasIndex(e => e.IdempotencyKey, "UX_INVOICE_idempotency_key").IsUnique().HasFilter("[Idempotency_key] IS NOT NULL");
            entity.HasIndex(e => e.InvoiceNumber, "UX_INVOICE_invoice_number").IsUnique();
            entity.Property(e => e.IdCounterparty).HasColumnName("Id_counterparty");
            entity.Property(e => e.IdPanel).HasColumnName("Id_panel");
            entity.Property(e => e.RemittanceRef).HasColumnName("Remittance_ref");
            entity.Property(e => e.IdDisbursement).HasColumnName("Id_disbursement");
            entity.HasOne(e => e.IdCounterpartyNavigation).WithMany()
                .HasForeignKey(e => e.IdCounterparty)
                .HasConstraintName("FK_INVOICE_id_counterparty");
            entity.HasOne(e => e.IdPanelNavigation).WithMany()
                .HasForeignKey(e => e.IdPanel)
                .HasConstraintName("FK_INVOICE_id_panel");
            entity.HasOne(e => e.RemittanceRefNavigation).WithMany()
                .HasForeignKey(e => e.RemittanceRef)
                .HasConstraintName("FK_INVOICE_remittance_ref");
            entity.HasOne(e => e.IdDisbursementNavigation).WithMany()
                .HasForeignKey(e => e.IdDisbursement)
                .HasConstraintName("FK_INVOICE_id_disbursement");
            entity.HasIndex(e => e.IdDisbursement, "UX_INVOICE_id_disbursement")
                .IsUnique()
                .HasFilter("[Id_disbursement] IS NOT NULL");
        });

        modelBuilder.Entity<InvoicePanelAllocation>(entity =>
        {
            entity.HasKey(e => e.IdInvoiceAllocation);
            entity.ToTable("INVOICE_PANEL_ALLOCATION");
            entity.Property(e => e.IdInvoiceAllocation).HasColumnName("id_invoice_allocation");
            entity.Property(e => e.IdInvoice).HasColumnName("id_invoice");
            entity.Property(e => e.IdPanel).HasColumnName("id_panel");
            entity.Property(e => e.IdCounterparty).HasColumnName("id_counterparty");
            entity.Property(e => e.ReinsurerNameSnapshot).HasMaxLength(255).IsRequired().HasColumnName("reinsurer_name_snapshot");
            entity.Property(e => e.SignedSharePctSnapshot).HasColumnType("decimal(19,8)").HasColumnName("signed_share_pct_snapshot");
            entity.Property(e => e.AllocationRatio).HasColumnType("decimal(19,8)").HasColumnName("allocation_ratio");
            entity.Property(e => e.GrossPremium).HasColumnType("decimal(19,4)").HasColumnName("gross_premium");
            entity.Property(e => e.CedentCommissionPct).HasColumnType("decimal(19,8)").HasColumnName("cedent_commission_pct");
            entity.Property(e => e.CedentCommissionAmount).HasColumnType("decimal(19,4)").HasColumnName("cedent_commission_amount");
            entity.Property(e => e.BrokeragePct).HasColumnType("decimal(19,8)").HasColumnName("brokerage_pct");
            entity.Property(e => e.BrokerageAmount).HasColumnType("decimal(19,4)").HasColumnName("brokerage_amount");
            entity.Property(e => e.OtherDeductions).HasColumnType("decimal(19,4)").HasColumnName("other_deductions");
            entity.Property(e => e.OtherDeductionsPercentage).HasColumnType("decimal(19,8)").HasColumnName("other_deductions_percentage");
            entity.Property(e => e.NetAmount).HasColumnType("decimal(19,4)").HasColumnName("net_amount");
            entity.Property(e => e.CurrencyCodeSnapshot).HasMaxLength(20).IsRequired().HasColumnName("currency_code_snapshot");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => new { e.IdInvoice, e.IdPanel }, "UX_INVOICE_PANEL_ALLOCATION_invoice_panel").IsUnique();
            entity.HasOne(e => e.IdInvoiceNavigation).WithMany(i => i.PanelAllocations).HasForeignKey(e => e.IdInvoice).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.IdPanelNavigation).WithMany().HasForeignKey(e => e.IdPanel).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(e => e.IdCounterpartyNavigation).WithMany().HasForeignKey(e => e.IdCounterparty).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Disbursement>(entity =>
        {
            entity.Property(e => e.BankReference).HasMaxLength(255).HasColumnName("Bank_reference");
            entity.Property(e => e.StartedAt).HasColumnName("Started_at");
            entity.Property(e => e.StartedBy).HasMaxLength(450).HasColumnName("Started_by");
            entity.Property(e => e.ClosedAt).HasColumnName("Closed_at");
            entity.Property(e => e.ClosedBy).HasMaxLength(450).HasColumnName("Closed_by");
            entity.Property(e => e.BankNameSnapshot).HasMaxLength(255).HasColumnName("Bank_name_snapshot");
            entity.Property(e => e.BankCitySnapshot).HasMaxLength(150).HasColumnName("Bank_city_snapshot");
            entity.Property(e => e.BankCountrySnapshot).HasMaxLength(150).HasColumnName("Bank_country_snapshot");
            entity.Property(e => e.IbanSnapshot).HasMaxLength(100).HasColumnName("Iban_snapshot");
            entity.Property(e => e.SwiftBicSnapshot).HasMaxLength(100).HasColumnName("Swift_bic_snapshot");
            entity.Property(e => e.CorrespondentBankNameSnapshot).HasMaxLength(255).HasColumnName("Correspondent_bank_name_snapshot");
            entity.Property(e => e.CorrespondentSwiftBicSnapshot).HasMaxLength(100).HasColumnName("Correspondent_swift_bic_snapshot");
            entity.Property(e => e.GrossPremiumSnapshot).HasColumnType("decimal(19, 4)").HasColumnName("Gross_premium_snapshot");
            entity.Property(e => e.DeductionsSnapshot).HasColumnType("decimal(19, 4)").HasColumnName("Deductions_snapshot");
            entity.Property(e => e.CurrencyCodeSnapshot).HasMaxLength(20).HasColumnName("Currency_code_snapshot");
            entity.Property(e => e.ContractNameSnapshot).HasMaxLength(255).HasColumnName("Contract_name_snapshot");
            entity.Property(e => e.UmrSnapshot).HasMaxLength(100).HasColumnName("Umr_snapshot");
            entity.Property(e => e.CedentNameSnapshot).HasMaxLength(255).HasColumnName("Cedent_name_snapshot");
            entity.Property(e => e.ReinsurerNameSnapshot).HasMaxLength(255).HasColumnName("Reinsurer_name_snapshot");
            entity.Property(e => e.ReinsurerAddressSnapshot).HasMaxLength(500).HasColumnName("Reinsurer_address_snapshot");
            entity.Property(e => e.ReinsurerCitySnapshot).HasMaxLength(150).HasColumnName("Reinsurer_city_snapshot");
            entity.Property(e => e.ReinsurerCountrySnapshot).HasMaxLength(150).HasColumnName("Reinsurer_country_snapshot");
            entity.Property(e => e.ReinsurerTaxIdSnapshot).HasMaxLength(100).HasColumnName("Reinsurer_tax_id_snapshot");
            entity.Property(e => e.PanelReferenceSnapshot).HasMaxLength(255).HasColumnName("Panel_reference_snapshot");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.IdNotification);
            entity.ToTable("NOTIFICATION");
            entity.HasIndex(e => new { e.RecipientUserId, e.IsRead }, "IX_NOTIFICATION_recipient_read");
            entity.Property(e => e.IdNotification).HasColumnName("id_notification");
            entity.Property(e => e.RecipientUserId).HasMaxLength(450).IsRequired().HasColumnName("recipient_user_id");
            entity.Property(e => e.RecipientRole).HasMaxLength(64).HasColumnName("recipient_role");
            entity.Property(e => e.NotificationType).HasMaxLength(64).IsRequired().HasColumnName("notification_type");
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(64).IsRequired().HasColumnName("entity_type");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.TargetRoute).HasMaxLength(255).HasColumnName("target_route");
            entity.Property(e => e.IsRead).HasDefaultValue(false).HasColumnName("is_read");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");
            entity.Property(e => e.ReadAt).HasColumnName("read_at");
            entity.Property(e => e.CreatedBy).HasMaxLength(450).HasColumnName("created_by");
        });

        modelBuilder.Entity<BankReceipt>(entity =>
        {
            entity.Property(e => e.BusinessReference).HasMaxLength(255).HasColumnName("Business_reference");
            entity.Property(e => e.DeclaredInvoiceNumber).HasMaxLength(100).HasColumnName("Declared_invoice_number");
            entity.Property(e => e.DeclaredInvoiceMonth).HasColumnName("Declared_invoice_month");
        });
    }
}
