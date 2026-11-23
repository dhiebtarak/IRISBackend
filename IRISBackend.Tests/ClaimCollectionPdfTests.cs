using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ClaimCollectionPdfTests
{
    [Theory]
    [InlineData(2250000, "USD", 2, "TWO MILLION TWO HUNDRED AND FIFTY THOUSAND DOLLARS")]
    [InlineData(750, "TND", 3, "SEVEN HUNDRED AND FIFTY TUNISIAN DINARS")]
    [InlineData(1.25, "EUR", 2, "ONE EURO AND TWENTY FIVE CENTS")]
    [InlineData(0.125, "TND", 3, "ZERO TUNISIAN DINARS AND ONE HUNDRED AND TWENTY FIVE MILLIMES")]
    [InlineData(1.9999, "USD", 2, "TWO DOLLARS")]
    public void EnglishCollectionAmountMatchesCurrencyPrecision(decimal amount, string currency, int precision, string expected)
        => Assert.Equal(expected, FinancialWorkflowService.CollectionAmountInWords(amount, currency, precision));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceLayoutPreservesAccountDetailsAndPaginatesLongText(bool longText)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var service = typeof(FinancialWorkflowService);
        var modelType = service.GetNestedType("IrisInvoiceAvoirPdfModel", System.Reflection.BindingFlags.NonPublic)!;
        var bankType = service.GetNestedType("IrisInvoiceBankAccount", System.Reflection.BindingFlags.NonPublic)!;
        var banks = Array.CreateInstance(bankType, 3);
        for (var index = 0; index < 3; index++)
        {
            var currency = new[] { "TND", "USD", "EUR" }[index];
            var iban = "TEST-IBAN-" + currency + (longText ? new string('9', 300) : "-0000000000000001");
            banks.SetValue(Activator.CreateInstance(bankType, ["SOCIETE IRIS TUNISIA", "TEST Bank", currency, "TEST-RIB-" + currency, iban, "TESTBIC", "Test branch Tunis"]), index);
        }
        var description = longText ? string.Join(" ", Enumerable.Repeat("Machinery breakdown - compressor damage", 100)) : "GE9FB Machinery Breakdown - GT1 Compressor Failure";
        var model = Activator.CreateInstance(modelType, [Enum.Parse(modelType.GetConstructors()[0].GetParameters()[0].ParameterType, "Avoir"),
            "AV-2026-000014", new DateOnly(2026,11,23), "Societe Commerciale Gabonaise de Reassurance", null, "Libreville", "Gabon", null,
            "Test contract", "TEST-UMR", "Claim settlement", "Machinery Breakdown", new DateOnly(2026,1,1), new DateOnly(2026,12,31),
            2250000m, "USD", 2, new IrisInvoiceBrandingOptions { CompanyName="IRIS Tunisia", AddressLine1="Immeuble Iris, 2eme etage", AddressLine2="Rue Malaren, Lac I, Tunisia", Phone="+216 71 797095" },
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Assets","Branding","iris-logo.png")), banks])!;
        var detailsType = service.GetNestedType("ClaimAccountingDetails", System.Reflection.BindingFlags.NonPublic)!;
        modelType.GetProperty("ClaimDetails")!.SetValue(model, Activator.CreateInstance(detailsType, ["CL-2026-000003", new DateOnly(2023,8,16),description,45000000m,0m,"Compagnie Algerienne Des Assurances (CAAT)",5m,true]));
        var pdf = (byte[])service.GetMethod("BuildIrisInvoiceAvoirPdf", flags)!.Invoke(null,[model])!;
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.Contains("Collection Note", text);
        Assert.Contains("2,250,000.00", text);
        Assert.DoesNotContain(" re S", text);
        foreach (var currency in new[] {"TND","USD","EUR"}) Assert.Contains("TEST-RIB-"+currency,text);
        if (longText) Assert.Contains("Collection Note \\(continued\\)", text);
        else Assert.Contains("/Count 1", text);
        var folder = Environment.GetEnvironmentVariable("IRIS_CLAIM_DOCUMENT_PREVIEW");
        if (!string.IsNullOrWhiteSpace(folder)) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder,longText ? "long-collection.pdf" : "reference-collection.pdf"),pdf); }
    }
}
