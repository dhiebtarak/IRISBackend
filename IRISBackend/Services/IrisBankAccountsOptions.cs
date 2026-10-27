namespace IRISBackend.Services;

public sealed class IrisBankAccountsOptions
{
    public const string SectionName = "IrisBankAccounts";
    public const int ExpectedFinancialNoteAccountCount = 3;

    public List<IrisBankAccountOption> Accounts { get; set; } = [];

    public List<string> GetFinancialNoteConfigurationErrors()
    {
        var errors = new List<string>();
        var active = Accounts
            .Select((account, index) => (Account: account, Index: index))
            .Where(item => item.Account.IsActive)
            .ToArray();

        if (active.Length != ExpectedFinancialNoteAccountCount)
            errors.Add($"Accounts: expected exactly {ExpectedFinancialNoteAccountCount} active accounts; found {active.Length}.");

        foreach (var (account, index) in active)
        {
            if (string.IsNullOrWhiteSpace(account.AccountHolder)) errors.Add($"Accounts[{index}].AccountHolder");
            if (string.IsNullOrWhiteSpace(account.BankName)) errors.Add($"Accounts[{index}].BankName");
            if (string.IsNullOrWhiteSpace(account.Currency)) errors.Add($"Accounts[{index}].Currency");
            if (string.IsNullOrWhiteSpace(account.Rib) && string.IsNullOrWhiteSpace(account.Iban))
                errors.Add($"Accounts[{index}].RIB or Accounts[{index}].IBAN");
            if (string.IsNullOrWhiteSpace(account.SwiftBic)) errors.Add($"Accounts[{index}].SwiftBic");
        }

        return errors;
    }
}

public sealed class IrisBankAccountOption
{
    public string BankName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string SwiftBic { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? AccountHolder { get; set; }
    public string? Rib { get; set; }
    public string? Domiciliation { get; set; }
    public int? DisplayOrder { get; set; }
}
