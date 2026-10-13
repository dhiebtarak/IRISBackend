namespace IRISBackend.Services;

public sealed class IrisBankAccountsOptions
{
    public const string SectionName = "IrisBankAccounts";

    public List<IrisBankAccountOption> Accounts { get; set; } = [];
}

public sealed class IrisBankAccountOption
{
    public string BankName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string SwiftBic { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
