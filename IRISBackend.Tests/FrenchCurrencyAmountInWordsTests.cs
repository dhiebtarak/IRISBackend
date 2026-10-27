using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using IRISBackend.Services;
using IRISBackend.Models;
using Xunit;

namespace IRISBackend.Tests;

public sealed class FrenchCurrencyAmountInWordsTests
{
    [Theory]
    [InlineData(1800.250, "TND", 3, "mille huit cents dinars tunisiens et deux cent cinquante millimes")]
    [InlineData(2000.000, "TND", 3, "deux mille dinars tunisiens")]
    [InlineData(1.001, "TND", 3, "un dinar tunisien et un millime")]
    [InlineData(1.50, "EUR", 2, "un euro et cinquante centimes")]
    [InlineData(2.01, "USD", 2, "deux dollars am\u00e9ricains et un cent")]
    [InlineData(199.9995, "TND", 3, "deux cents dinars tunisiens")]
    public void ConvertsRoundedCurrencyAmountsToFrenchWords(
        decimal amount, string currency, int precision, string expected)
    {
        var converter = typeof(FinancialWorkflowService).Assembly
            .GetType("IRISBackend.Services.FrenchCurrencyAmountInWords")!
            .GetMethod("Convert", BindingFlags.Public | BindingFlags.Static)!;

        var result = (string)converter.Invoke(null, [amount, currency, precision])!;

        Assert.Equal(expected, result);
    }

    [Fact]
    public void SharedPdfAddsWordsBelowInvoiceAndAvoirAmounts()
    {
        var invoicePdf = RenderPdf(documentType: 0, amount: 1800.250m, currency: "TND", precision: 3);
        var avoirPdf = RenderPdf(documentType: 1, amount: -2000m, currency: "TND", precision: 3);
        var invoiceContent = Encoding.Latin1.GetString(invoicePdf);
        var avoirContent = Encoding.Latin1.GetString(avoirPdf);

        Assert.Contains("Arr\u00eat\u00e9e la pr\u00e9sente facture \u00e0 la somme de :", invoiceContent);
        Assert.Contains("mille huit cents dinars", invoiceContent);
        Assert.Contains("tunisiens", invoiceContent);
        var textOperations = Regex.Matches(invoiceContent, @"\(([^)]*)\) Tj")
            .Select(match => match.Groups[1].Value);
        Assert.Contains("deux cent cinquante millimes", string.Join(" ", textOperations));
        Assert.Contains("Arr\u00eat\u00e9 le pr\u00e9sent avoir \u00e0 la somme de :", avoirContent);
        Assert.Contains("deux mille dinars tunisiens", avoirContent);
        Assert.DoesNotContain("moins deux mille", avoirContent);
        Assert.Contains("62 340 Tm", invoiceContent);
        Assert.Contains("62 340 Tm", avoirContent);
    }

    private static byte[] RenderPdf(int documentType, decimal amount, string currency, int precision)
    {
        var serviceAssembly = typeof(FinancialWorkflowService).Assembly;
        var modelType = serviceAssembly.GetType(
            "IRISBackend.Services.FinancialWorkflowService+IrisInvoiceAvoirPdfModel")!;
        var enumType = serviceAssembly.GetType(
            "IRISBackend.Services.FinancialWorkflowService+IrisAccountingDocumentType")!;
        var modelParameters = modelType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 20).GetParameters();
        var bankAccountType = modelParameters[^1].ParameterType.GenericTypeArguments[0];
        var accounts = Array.CreateInstance(bankAccountType, 0);
        var model = Activator.CreateInstance(
            modelType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                Enum.ToObject(enumType, documentType), "DOC-1", new DateOnly(2026, 10, 27),
                "Cedent", null, null, null, null, "Contract", "UMR-1", "Premium",
                "Property", null, null, amount, currency, precision,
                new IrisInvoiceBrandingOptions { CompanyName = "IRIS" }, null, accounts
            ],
            culture: null)!;
        var renderer = typeof(FinancialWorkflowService).GetMethod(
            "BuildIrisInvoiceAvoirPdf", BindingFlags.Static | BindingFlags.NonPublic)!;

        return (byte[])renderer.Invoke(null, [model])!;
    }
}
