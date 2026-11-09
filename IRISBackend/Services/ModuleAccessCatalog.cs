namespace IRISBackend.Services;

public static class ModuleAccessCatalog
{
    public const string ClaimType = "iris:module";
    public const string ConfigurationClaimType = "iris:modules-configured";

    public const string Counterparties = "Counterparties";
    public const string Contracts = "Contracts";
    public const string Claims = "Claims";
    public const string BackOffice = "BackOffice";
    public const string Encaissement = "Encaissement";
    public const string Decaissement = "Decaissement";
    public const string CounterpartyBankAccounts = "CounterpartyBankAccounts";
    public const string PolicyPrefix = "Module:";
    public const string ContractReadPolicy = "Module:ContractRead";
    public const string CounterpartyReadPolicy = "Module:CounterpartyRead";

    public static readonly IReadOnlyList<string> All =
    [
        Counterparties,
        Contracts,
        Claims,
        BackOffice,
        Encaissement,
        Decaissement,
        CounterpartyBankAccounts
    ];

    public static bool IsValid(string? module) =>
        !string.IsNullOrWhiteSpace(module) &&
        All.Contains(module.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Canonicalize(string module) =>
        All.First(value => value.Equals(module.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Policy(string module) => PolicyPrefix + Canonicalize(module);

    public static IReadOnlyList<string> LegacyModules(string department, string position)
    {
        if (!string.Equals(department?.Trim(), "Reassurance", StringComparison.OrdinalIgnoreCase)) return [];

        return position?.Trim().ToLowerInvariant() switch
        {
            "front officer" => [Counterparties, Contracts, Claims],
            "back officer" => [BackOffice],
            "finance officer" => [Encaissement, Decaissement, CounterpartyBankAccounts],
            _ => []
        };
    }
}
