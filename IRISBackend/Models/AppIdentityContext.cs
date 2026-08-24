using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Models
{
    public class AppIdentityContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public AppIdentityContext(DbContextOptions<AppIdentityContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(user => user.FullName)
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasDefaultValue(string.Empty);

                entity.Property(user => user.Department)
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasDefaultValue(string.Empty);

                entity.Property(user => user.Position)
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasDefaultValue(string.Empty);
            });
        }
    }
}
