using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    private static void ConfigureClaimsWorkflow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentAttachment>(e =>
        {
            e.Property(x => x.IdClaim).HasColumnName("Id_claim");
            e.HasIndex(x => x.IdClaim).HasDatabaseName("IX_DOCUMENT_ATTACHMENT_Id_claim");
            e.HasOne<Claim>().WithMany().HasForeignKey(x => x.IdClaim)
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_DOCUMENT_ATTACHMENT_CLAIM");
        });
        modelBuilder.Entity<Claim>(e =>
        {
            e.Property(x => x.OtherFees).HasColumnType("decimal(19,4)").HasColumnName("Other_fees");
            e.Property(x => x.SignedShareSnapshot).HasColumnType("decimal(19,8)").HasColumnName("Signed_share_snapshot");
            e.Property(x => x.CollectionAmount).HasColumnType("decimal(19,4)").HasColumnName("Collection_amount");
            e.Property(x => x.ProcessingMode).HasMaxLength(40).HasColumnName("Processing_mode");
            e.Property(x => x.CollectionReference).HasMaxLength(100).HasColumnName("Collection_reference");
            e.Property(x => x.CollectionIssuedAt).HasColumnName("Collection_issued_at");
            e.Property(x => x.CollectionIssuedBy).HasMaxLength(450).HasColumnName("Collection_issued_by");
            e.Property(x => x.ContractNameSnapshot).HasMaxLength(255).HasColumnName("Contract_name_snapshot");
            e.Property(x => x.UmrSnapshot).HasMaxLength(100).HasColumnName("Umr_snapshot");
            e.Property(x => x.CurrencyCodeSnapshot).HasMaxLength(20).HasColumnName("Currency_code_snapshot");
            e.Property(x => x.CollectionPdf).HasColumnType("varbinary(max)").HasColumnName("Collection_pdf");
            e.HasIndex(x => x.ClaimNumber).IsUnique().HasFilter("[Claim_number] IS NOT NULL").HasDatabaseName("UX_CLAIM_number");
        });
        modelBuilder.Entity<CollectionNote>(e =>
        {
            e.Property(x => x.IdPanel).HasColumnName("Id_panel");
            e.Property(x => x.IdCurrency).HasColumnName("Id_currency");
            e.Property(x => x.ReinsurerNameSnapshot).HasMaxLength(255).HasColumnName("Reinsurer_name_snapshot");
            e.HasOne<ContractPanel>().WithMany().HasForeignKey(x => x.IdPanel).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Currency>().WithMany().HasForeignKey(x => x.IdCurrency).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.IdClaim, x.IdPanel }).IsUnique().HasFilter("[Id_panel] IS NOT NULL").HasDatabaseName("UX_COLLECTION_NOTE_claim_panel");
        });
        modelBuilder.Entity<ReceiptAllocation>().HasIndex(x => x.ClaimRef).IsUnique()
            .HasFilter("[Claim_ref] IS NOT NULL AND [is_active] = 1").HasDatabaseName("UX_RECEIPT_ALLOCATION_active_claim");
        modelBuilder.Entity<RemittanceRef>().HasIndex(x => new { x.ClaimRef, x.IdPanel }).IsUnique()
            .HasFilter("[Claim_ref] IS NOT NULL AND [Is_active] = 1").HasDatabaseName("UX_REMITTANCE_REF_active_claim_panel");
    }
}
