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
    }
}
