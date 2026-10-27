namespace IRISBackend.Services;

public static class InvoiceRecoveryCalculator
{
    public const string Unsettled = "Unsettled";
    public const string PartiallySettled = "PartiallySettled";
    public const string Settled = "Settled";

    public static InvoiceRecoveryAmounts Calculate(decimal grossAmount, decimal pairedAmount)
    {
        var outstanding = Math.Max(0m, grossAmount - pairedAmount);
        return new InvoiceRecoveryAmounts(
            pairedAmount,
            outstanding,
            GetInvoicePaymentStatus(grossAmount, pairedAmount));
    }

    public static string GetInvoicePaymentStatus(decimal invoiceAmount, decimal settledAmount)
    {
        if (settledAmount <= 0m) return Unsettled;
        return settledAmount >= invoiceAmount ? Settled : PartiallySettled;
    }

    public static string GetReceiptPairingStatus(decimal receiptAmount, decimal allocatedAmount)
    {
        if (allocatedAmount <= 0m) return "UNPAIRED";
        return receiptAmount > 0m && allocatedAmount >= receiptAmount ? "PAIRED" : "PARTIALLY_PAIRED";
    }
}

public sealed record InvoiceRecoveryAmounts(decimal PairedAmount, decimal OutstandingAmount, string Status);
