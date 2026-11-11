using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    public DbSet<CounterpartyContact> CounterpartyContacts { get; set; }

    private static void ConfigureCounterpartyContacts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CounterpartyContact>(entity =>
        {
            entity.ToTable("COUNTERPARTY_CONTACT");
            entity.HasKey(contact => contact.Id);
            entity.Property(contact => contact.Email).HasMaxLength(256);
            entity.Property(contact => contact.PhoneNumber).HasMaxLength(32);
            entity.HasIndex(contact => new { contact.CounterpartyId, contact.SortOrder });
            entity.HasOne(contact => contact.Counterparty).WithMany(counterparty => counterparty.AdditionalContacts)
                .HasForeignKey(contact => contact.CounterpartyId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
