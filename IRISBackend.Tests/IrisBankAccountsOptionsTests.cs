using IRISBackend.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IRISBackend.Tests;

public sealed class IrisBankAccountsOptionsTests
{
    [Fact]
    public void Existing_secret_key_names_bind_and_validate_with_legacy_property_casing()
    {
        var values = new Dictionary<string, string?>
        {
            ["IrisBankAccounts:Accounts:0:BankName"] = "Bank A",
            ["IrisBankAccounts:Accounts:0:Currency"] = "TND",
            ["IrisBankAccounts:Accounts:0:Iban"] = "configured-but-not-logged",
            ["IrisBankAccounts:Accounts:0:SwiftBic"] = "TESTTN01",
            ["IrisBankAccounts:Accounts:0:IsActive"] = "true",
            ["IrisBankAccounts:Accounts:0:AccountHolder"] = "IRIS",
            ["IrisBankAccounts:Accounts:0:DisplayOrder"] = "1",
            ["IrisBankAccounts:Accounts:1:BankName"] = "Bank B",
            ["IrisBankAccounts:Accounts:1:Currency"] = "USD",
            ["IrisBankAccounts:Accounts:1:Iban"] = "configured-but-not-logged",
            ["IrisBankAccounts:Accounts:1:SwiftBic"] = "TESTUS01",
            ["IrisBankAccounts:Accounts:1:IsActive"] = "true",
            ["IrisBankAccounts:Accounts:1:AccountHolder"] = "IRIS",
            ["IrisBankAccounts:Accounts:1:DisplayOrder"] = "2",
            ["IrisBankAccounts:Accounts:2:BankName"] = "Bank C",
            ["IrisBankAccounts:Accounts:2:Currency"] = "EUR",
            ["IrisBankAccounts:Accounts:2:Iban"] = "configured-but-not-logged",
            ["IrisBankAccounts:Accounts:2:SwiftBic"] = "TESTEU01",
            ["IrisBankAccounts:Accounts:2:IsActive"] = "true",
            ["IrisBankAccounts:Accounts:2:AccountHolder"] = "IRIS",
            ["IrisBankAccounts:Accounts:2:DisplayOrder"] = "3"
        };

        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build()
            .GetSection(IrisBankAccountsOptions.SectionName)
            .Get<IrisBankAccountsOptions>()!;

        Assert.Equal(3, options.Accounts.Count);
        Assert.Equal(new int?[] { 1, 2, 3 }, options.Accounts.Select(account => account.DisplayOrder));
        Assert.Empty(options.GetFinancialNoteConfigurationErrors());
    }

    [Fact]
    public void Invalid_configuration_reports_missing_field_paths_without_values()
    {
        var options = new IrisBankAccountsOptions
        {
            Accounts =
            [
                new() { BankName = "Bank A", Currency = "TND", Iban = "secret", SwiftBic = "BIC", IsActive = true },
                new() { BankName = "Bank B", Currency = "USD", Iban = "secret", SwiftBic = "BIC", IsActive = true },
                new() { BankName = "Bank C", Currency = "EUR", Iban = "secret", SwiftBic = "BIC", IsActive = false }
            ]
        };

        var errors = options.GetFinancialNoteConfigurationErrors();

        Assert.Contains("Accounts: expected exactly 3 active accounts; found 2.", errors);
        Assert.Contains("Accounts[0].AccountHolder", errors);
        Assert.Contains("Accounts[1].AccountHolder", errors);
        Assert.DoesNotContain(errors, error => error.Contains("secret", StringComparison.Ordinal));
    }
}
