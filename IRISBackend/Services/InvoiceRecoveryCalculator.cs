namespace IRISBackend.Services;

public static class InvoiceRecoveryCalculator
{
    public static InvoiceRecoveryAmounts Calculate(decimal grossAmount, decimal pairedAmount)
    {
        var outstanding = Math.Max(0m, grossAmount - pairedAmount);
        return new InvoiceRecoveryAmounts(
            pairedAmount,
            outstanding,
            grossAmount > 0m && pairedAmount >= grossAmount ? "Paired" : "Unpaired");
    }

    public static string GetReceiptPairingStatus(decimal receiptAmount, decimal allocatedAmount) =>
        receiptAmount > 0m && allocatedAmount >= receiptAmount ? "Paired" : "Unpaired";
}

public sealed record InvoiceRecoveryAmounts(decimal PairedAmount, decimal OutstandingAmount, string Status);
