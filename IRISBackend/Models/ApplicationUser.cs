using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace IRISBackend.Models
{
    public class ApplicationUser : IdentityUser
    {
        [PersonalData]
        [Column(TypeName = "nvarchar(100)")]
        public string FullName { get; set; } = string.Empty;

        [PersonalData]
        [Column(TypeName = "nvarchar(100)")]
        public string Department { get; set; } = string.Empty;

        [PersonalData]
        [Column(TypeName = "nvarchar(100)")]
        public string Position { get; set; } = string.Empty;
    }
}
