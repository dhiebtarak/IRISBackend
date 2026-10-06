using IRISBackend.Models;

namespace IRISBackend.Services;

public sealed record BankAccountSelectionResult(CounterpartyBankAccount? Account, string? ValidationMessage)
{
    public bool IsValid => Account is not null && ValidationMessage is null;
}

public static class BankAccountEligibility
{
    public const string NoMatchingAccountMessage =
        "No active bank account was found for this reinsurer in the remittance currency.";

    public const string MultipleMatchingAccountsMessage =
        "Multiple active bank accounts were found for this reinsurer and currency. A bank account must be selected explicitly.";

    public const string MissingPaymentDetailsMessage =
        "The active bank account must contain an IBAN and SWIFT/BIC.";

    public static BankAccountSelectionResult Select(IReadOnlyList<CounterpartyBankAccount> accounts)
    {
        if (accounts.Count == 0)
            return new(null, NoMatchingAccountMessage);

        if (accounts.Count > 1)
            return new(null, MultipleMatchingAccountsMessage);

        var account = accounts[0];
        return string.IsNullOrWhiteSpace(account.Iban) || string.IsNullOrWhiteSpace(account.SwiftBic)
            ? new(account, MissingPaymentDetailsMessage)
            : new(account, null);
    }
}
