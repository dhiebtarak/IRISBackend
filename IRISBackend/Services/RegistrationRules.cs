using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    internal static class RegistrationRules
    {
        private static readonly string[] Departments = ["Reassurance", "Operation"];
        private static readonly string[] ReassurancePositions = ["Front officer", "Back officer", "Finance officer"];

        public static IEnumerable<string> Validate(RegisterRequestDto request)
        {
            if (!Departments.Contains(request.Department, StringComparer.OrdinalIgnoreCase))
            {
                yield return "Department must be Reassurance or Operation.";
            }

            if (request.Department.Equals("Reassurance", StringComparison.OrdinalIgnoreCase) &&
                !ReassurancePositions.Contains(request.Position, StringComparer.OrdinalIgnoreCase))
            {
                yield return "Reassurance position must be Front officer, Back officer, or Finance officer.";
            }
        }
    }
}
