using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class InvoiceRecoveryCalculatorTests
{
    [Theory]
    [InlineData(500, 500, 0, "Settled")]
    [InlineData(250, 250, 0, "Settled")]
    [InlineData(250, 100, 150, "PartiallySettled")]
    [InlineData(50, 0, 50, "Unsettled")]
    [InlineData(0, 0, 0, "Unsettled")]
    public void Recovery_status_and_outstanding_use_gross_invoice_amount(
        decimal grossAmount,
        decimal pairedAmount,
        decimal expectedOutstanding,
        string expectedStatus)
    {
        var result = InvoiceRecoveryCalculator.Calculate(grossAmount, pairedAmount);

        Assert.Equal(pairedAmount, result.PairedAmount);
        Assert.Equal(expectedOutstanding, result.OutstandingAmount);
        Assert.Equal(expectedStatus, result.Status);
    }

    [Theory]
    [InlineData(1000, 0, "Unsettled")]
    [InlineData(1000, 0.01, "PartiallySettled")]
    [InlineData(1000, 999.99, "PartiallySettled")]
    [InlineData(1000, 1000, "Settled")]
    [InlineData(1000, 1001, "Settled")]
    public void Invoice_payment_status_uses_settled_amount_thresholds(decimal amount, decimal settled, string expected)
    {
        Assert.Equal(expected, InvoiceRecoveryCalculator.GetInvoicePaymentStatus(amount, settled));
    }

    [Theory]
    [InlineData(1000, 999.99, "Unpaired")]
    [InlineData(1000, 1000, "Paired")]
    [InlineData(1000, 1001, "Paired")]
    public void Receipt_exposes_only_unpaired_or_paired_status(decimal amount, decimal allocated, string expected)
    {
        Assert.Equal(expected, InvoiceRecoveryCalculator.GetReceiptPairingStatus(amount, allocated));
    }
}
