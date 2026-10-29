using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public static class RegistrationRules
    {
        private static readonly IReadOnlyDictionary<string, string[]> DepartmentPositions =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Reassurance"] = ["Front officer", "Back officer", "Finance officer"]
            };

        public static IEnumerable<string> Validate(RegisterRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
            {
                yield return "FullName: Full name must contain at least 2 characters.";
            }

            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Trim().Length < 4)
            {
                yield return "Username: Username must contain at least 4 characters.";
            }

            if (string.IsNullOrWhiteSpace(request.Email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
                yield return "Email: Enter a valid email address.";

            if (string.IsNullOrWhiteSpace(request.Password) ||
                !System.Text.RegularExpressions.Regex.IsMatch(request.Password, "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[@$!%*?&])[A-Za-z\\d@$!%*?&]{6,}$"))
                yield return "Password: Use at least 6 characters with uppercase, lowercase, a number, and a special character.";

            if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
                yield return "ConfirmPassword: Password and confirmation must match.";

            if (string.IsNullOrWhiteSpace(request.Department) || !DepartmentPositions.TryGetValue(request.Department.Trim(), out var positions))
            {
                yield return "Department: Select a supported department.";
                yield break;
            }

            if (string.IsNullOrWhiteSpace(request.Position) || !positions.Contains(request.Position.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                yield return "Position: Select Front officer, Back officer, or Finance officer.";
            }
        }

        public static (string Department, string Position) Normalize(string department, string position)
        {
            var canonicalDepartment = DepartmentPositions.Keys.First(value => value.Equals(department.Trim(), StringComparison.OrdinalIgnoreCase));
            var canonicalPosition = DepartmentPositions[canonicalDepartment]
                .First(value => value.Equals(position.Trim(), StringComparison.OrdinalIgnoreCase));
            return (canonicalDepartment, canonicalPosition);
        }
    }
}
