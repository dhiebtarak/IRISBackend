using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    public DbSet<OutgoingClosing> OutgoingClosings => Set<OutgoingClosing>();
    public DbSet<OutgoingClosingLine> OutgoingClosingLines => Set<OutgoingClosingLine>();
    public DbSet<InvoicePairingOperation> InvoicePairingOperations => Set<InvoicePairingOperation>();

    private static void ConfigureOutgoingRemittances(ModelBuilder builder)
    {
        builder.Entity<Notification>().Property(x => x.ClosingId).HasColumnName("closing_id");
        builder.Entity<Notification>().HasOne<OutgoingClosing>().WithMany().HasForeignKey(x => x.ClosingId).OnDelete(DeleteBehavior.NoAction);
        builder.Entity<ReceiptAllocation>().Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Entity<InvoicePairingOperation>(entity =>
        {
            entity.ToTable("INVOICE_PAIRING_OPERATION");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
            entity.Property(x => x.CreatedBy).HasMaxLength(450);
            entity.Property(x => x.PairingStatus).HasMaxLength(32);
            entity.Property(x => x.TotalAllocated).HasPrecision(19, 4);
            entity.Property(x => x.RemainingAmount).HasPrecision(19, 4);
            entity.HasIndex(x => new { x.ReceiptId, x.RequestHash }).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasOne<BankReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<AspNetUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<InvoicePairingOperationLine>(entity =>
        {
            entity.ToTable("INVOICE_PAIRING_OPERATION_LINE");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RequestedAmount).HasPrecision(19, 4);
            entity.HasOne(x => x.Operation).WithMany(x => x.Lines).HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Allocation).WithMany().HasForeignKey(x => x.ReceiptAllocationId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.OperationId, x.ReceiptAllocationId }).IsUnique();
        });
        builder.Entity<RemittanceRef>(entity =>
        {
            entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(x => x.IdInvoiceAllocation).HasColumnName("id_invoice_allocation");
            entity.HasOne(x => x.IdInvoiceAllocationNavigation).WithMany()
                .HasForeignKey(x => x.IdInvoiceAllocation).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<OutgoingClosing>(entity =>
        {
            entity.ToTable("OUTGOING_CLOSING");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reference).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("AwaitingFinanceDisbursement");
            entity.HasOne<Disbursement>().WithMany().HasForeignKey(x => x.IdDisbursement).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => x.IdDisbursement).IsUnique().HasFilter("[IdDisbursement] IS NOT NULL");
            entity.HasIndex(x => x.Reference).IsUnique();
            entity.Property(x => x.CreatedBy).HasMaxLength(450);
            entity.Property(x => x.TotalAmount).HasPrecision(19, 4);
            entity.Property(x => x.TotalInWords).HasMaxLength(1000);
            entity.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.IdCounterparty).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Currency>().WithMany().HasForeignKey(x => x.IdCurrency).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<AspNetUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<OutgoingClosingLine>(entity =>
        {
            entity.ToTable("OUTGOING_CLOSING_LINE");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PayableAmount).HasPrecision(19, 4);
            entity.HasOne(x => x.Closing).WithMany(x => x.Lines).HasForeignKey(x => x.ClosingId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Remittance).WithMany(x => x.ClosingLines).HasForeignKey(x => x.RemittanceId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => x.RemittanceId).IsUnique().HasFilter("[IsActive] = 1");
            entity.HasIndex(x => new { x.ClosingId, x.RemittanceId }).IsUnique();
        });
    }
}
