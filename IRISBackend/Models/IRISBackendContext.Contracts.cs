using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
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
    }
}
