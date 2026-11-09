using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace IRISBackend.Models;

public partial class IRISBackendContext
{
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    private static void ConfigureAccessActivity(ModelBuilder builder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Entity<AuthSession>(e =>
        {
            e.ToTable("AUTH_SESSION", t => t.HasCheckConstraint("CK_AUTH_SESSION_Status", "[Status] IN ('Active','LoggedOut','Expired','Revoked')"));
            e.HasKey(s => s.Id);
            e.Property(s => s.LoginAtUtc).HasConversion(utc);
            e.Property(s => s.LastActivityAtUtc).HasConversion(utc);
            e.Property(s => s.LogoutAtUtc).HasConversion(utc);
            e.Property(s => s.ExpiresAtUtc).HasConversion(utc);
            e.Property(s => s.RevokedAtUtc).HasConversion(utc);
            e.Property(s => s.CreatedAtUtc).HasConversion(utc);
            e.Property(s => s.UpdatedAtUtc).HasConversion(utc);
            e.Property(s => s.UserId).HasMaxLength(450).IsRequired();
            e.Property(s => s.SessionIdentifier).HasMaxLength(64).IsRequired();
            e.Property(s => s.Status).HasMaxLength(20).IsRequired();
            e.Property(s => s.RevokedByUserId).HasMaxLength(450);
            e.Property(s => s.RevocationReason).HasMaxLength(200);
            e.Property(s => s.IpAddress).HasMaxLength(64);
            e.Property(s => s.UserAgent).HasMaxLength(512);
            e.Property(s => s.Browser).HasMaxLength(50);
            e.Property(s => s.OperatingSystem).HasMaxLength(50);
            e.Property(s => s.DeviceType).HasMaxLength(30);
            e.HasIndex(s => s.SessionIdentifier).IsUnique();
            e.HasIndex(s => new { s.UserId, s.LoginAtUtc });
            e.HasIndex(s => new { s.Status, s.ExpiresAtUtc });
            e.HasIndex(s => s.LoginAtUtc);
            e.HasIndex(s => s.LastActivityAtUtc);
            e.HasIndex(s => s.RevokedAtUtc);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AspNetUser>().WithMany().HasForeignKey(s => s.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserLoginAudit>(e =>
        {
            e.Property(a => a.CreatedAtUtc).HasConversion(utc);
            e.Property(a => a.EventType).HasMaxLength(30).HasDefaultValue("LoginSuccessful");
            e.Property(a => a.ActorUserId).HasMaxLength(450);
            e.HasIndex(a => new { a.UserId, a.CreatedAtUtc });
            e.HasIndex(a => new { a.CreatedAtUtc, a.EventType });
            e.HasIndex(a => new { a.SessionId, a.CreatedAtUtc });
            e.HasOne<AuthSession>().WithMany().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AspNetUser>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
