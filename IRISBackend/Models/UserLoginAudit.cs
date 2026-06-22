using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IRISBackend.Models
{
    public class UserLoginAudit
    {
        public int Id { get; set; }

        public string? UserId { get; set; }

        [MaxLength(256)]
        public string EmailOrUsername { get; set; } = string.Empty;

        public bool Succeeded { get; set; }

        [MaxLength(200)]
        public string? FailureReason { get; set; }

        [MaxLength(64)]
        public string? IpAddress { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }
    }
}
