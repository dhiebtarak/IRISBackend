using IRISBackend.Models;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class BankAccountEligibilityTests
{
    [Fact]
    public void SelectsSingleActiveMatchingAccountEvenWhenNotDefault()
    {
        var account = Account(7, isDefault: false);

        var result = BankAccountEligibility.Select([account]);

        Assert.True(result.IsValid);
        Assert.Same(account, result.Account);
    }

    [Fact]
    public void RejectsWhenNoMatchingActiveAccountExists()
    {
        var result = BankAccountEligibility.Select([]);

        Assert.False(result.IsValid);
        Assert.Equal(BankAccountEligibility.NoMatchingAccountMessage, result.ValidationMessage);
    }

    [Fact]
    public void RejectsAmbiguousMatchingAccounts()
    {
        var result = BankAccountEligibility.Select([Account(7, false), Account(8, true)]);

        Assert.False(result.IsValid);
        Assert.Null(result.Account);
        Assert.Equal(BankAccountEligibility.MultipleMatchingAccountsMessage, result.ValidationMessage);
    }

    [Fact]
    public void RejectsSingleAccountWithoutPaymentDetails()
    {
        var account = Account(7, false);
        account.Iban = null;

        var result = BankAccountEligibility.Select([account]);

        Assert.False(result.IsValid);
        Assert.Equal(BankAccountEligibility.MissingPaymentDetailsMessage, result.ValidationMessage);
    }

    private static CounterpartyBankAccount Account(int id, bool isDefault) => new()
    {
        IdBankAccount = id,
        IsActive = true,
        IsDefault = isDefault,
        Iban = "TN5901000000000000000000",
        SwiftBic = "BIATTNTT"
    };
}
