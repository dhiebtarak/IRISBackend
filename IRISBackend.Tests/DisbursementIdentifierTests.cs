using Xunit;

namespace IRISBackend.Tests;

public sealed class DisbursementIdentifierTests
{
    [Fact]
    public void InvoiceDownloadUrlUsesDisbursementIdNotRemittanceId()
    {
        const int remittanceId = 2;
        const int disbursementId = 7;

        var url = $"/api/finance/disbursements/{disbursementId}/invoice";

        Assert.Equal("/api/finance/disbursements/7/invoice", url);
        Assert.DoesNotContain($"/{remittanceId}/invoice", url);
    }
}
