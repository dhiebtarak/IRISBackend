namespace IRISBackend.Services
{
    public static class IdentityRoles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Employee = "Employee";

        public static readonly string[] All = [Admin, Manager, Employee];
    }
}
