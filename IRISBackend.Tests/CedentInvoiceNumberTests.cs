using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class CedentInvoiceNumberTests
{
    [Fact]
    public void Format_UsesInvoicePrefixAndSixDigitSequence()
    {
        Assert.Equal("INV-2026-000001", CedentInvoiceNumber.Format(2026, 1));
        Assert.Equal("INV-2026-000123", CedentInvoiceNumber.Format(2026, 123));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000000)]
    public void Format_RejectsSequenceOutsideSixDigitRange(int sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CedentInvoiceNumber.Format(2026, sequence));
    }
}
