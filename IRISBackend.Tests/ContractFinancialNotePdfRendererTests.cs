using IRISBackend.Models;
using IRISBackend.Services;
using System.Collections;
using System.Reflection;
using System.Text;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ContractFinancialNotePdfRendererTests
{
    [Fact]
    public void ContractDebitNoteRendersReadableFinancialValuesAndAllConfiguredBankAccounts()
    {
        var pdf = RenderContractNote("DEBIT NOTE", 0m, 500m, 450m);
        var content = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", content);
        Assert.Contains("q 180 0 0 120 48 720 cm /Logo Do Q", content);
        Assert.Contains("ISSUED TO", content);
        Assert.Contains("CONTRACT INFORMATION", content);
        Assert.Contains("DEBIT NOTE SUMMARY", content);
        Assert.Contains("Amount Due from Cedent", content);
        Assert.Contains("500,000", content);
        Assert.Contains("450,000", content);
        Assert.Contains("Test Bank TND", content);
        Assert.Contains("Test Bank USD", content);
        Assert.Contains("Test Bank EUR", content);
        Assert.Contains("TNTESTIBAN0000000000000001", content);
        Assert.Contains("TNTESTIBAN0000000000000002", content);
        Assert.Contains("TNTESTIBAN0000000000000003", content);
        Assert.Contains("RIB: RIB-TEST-0001", content);
        Assert.DoesNotContain("RIB: ) Tj", content);
        Assert.DoesNotContain("58 247 m 537 247 l S", content);
        Assert.DoesNotContain("58 202 m 537 202 l S", content);
        Assert.Equal(1, Count(content, "In order to comply with the payment terms defined by reinsurers, please make sure to remit the payment by : .../.../......"));
        Assert.DoesNotContain("Please remit payment by bank transfer", content);
        Assert.DoesNotContain("Payment terms: Not provided", content);
        Assert.DoesNotContain("Website: Not provided", content);
        Assert.DoesNotContain("Tax ID:", content);
        Assert.DoesNotContain("Commercial Register:", content);
        Assert.DoesNotContain("Reference: DN-TEST-UMR-2026", content);
        Assert.Contains("/Count 1", content);
        Assert.Contains("0.07 0.12 0.19 rg BT", content);

        var previewPath = Environment.GetEnvironmentVariable("IRIS_PDF_PREVIEW_PATH");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
            File.WriteAllBytes(previewPath, pdf);
        }
    }

    [Fact]
    public void ContractCreditNoteShowsAvoirAdjustmentAsNegativeAndDoesNotIncludeInvoiceData()
    {
        var pdf = RenderContractNote("CREDIT NOTE", 40m, 500m, 450m);
        var content = Encoding.Latin1.GetString(pdf);

        Assert.Contains("CREDIT NOTE SUMMARY", content);
        Assert.Contains("Avoir Adjustment", content);
        Assert.Contains("-40,000", content);
        Assert.Contains("Amount After Avoir", content);
        Assert.DoesNotContain("Invoice Number", content);
        Assert.DoesNotContain("Invoice Business Number", content);
    }

    private static byte[] RenderContractNote(string title, decimal avoirAmount, decimal gross, decimal baseAmount)
    {
        var renderer = typeof(FinancialWorkflowService).GetMethod(
            "BuildFrenchContractFinancialNotePdf", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Active contract financial-note renderer was not found.");
        var bankAccountType = renderer.GetParameters()[15].ParameterType.GetGenericArguments()[0];
        var bankAccounts = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(bankAccountType))!;
        var constructor = bankAccountType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.GetParameters().Length == 7);
        bankAccounts.Add(constructor.Invoke(["IRIS Tunisia", "Test Bank TND", "TND", "RIB-TEST-0001", "TNTESTIBAN0000000000000001", "TESTTN01", "Tunis"]));
        bankAccounts.Add(constructor.Invoke(["IRIS Tunisia", "Test Bank USD", "USD", null, "TNTESTIBAN0000000000000002", "TESTUS01", "Tunis"]));
        bankAccounts.Add(constructor.Invoke(["IRIS Tunisia", "Test Bank EUR", "EUR", null, "TNTESTIBAN0000000000000003", "TESTEU01", "Tunis"]));

        var contract = new Contract
        {
            Name = "Test insured property",
            Umr = "TEST-UMR-2026",
            InceptionDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2026, 12, 31),
            IdCurrencyNavigation = new Currency { CurrencyCode = "TND" }
        };
        var cedent = new Counterparty
        {
            Name = "Test Cedent",
            ContactName = "Account Contact",
            AddressLine = "1 Example Street",
            City = "Tunis",
            Country = "Tunisia"
        };
        var branding = new IrisInvoiceBrandingOptions
        {
            CompanyName = "IRIS Tunisia",
            AddressLine1 = "Immeuble Iris, 2eme etage",
            AddressLine2 = "Lac 1, Tunis, Tunisie",
            Phone = "+216 71 000 000",
            Website = "Not provided",
            TaxIdentifier = "TEST-TAX",
            CommercialRegister = "TEST-RC"
        };
        var workspaceRoot = FindWorkspaceRoot();
        var logoPath = Path.Combine(workspaceRoot, "IRISBackend", "Assets", "Branding", "iris-logo.png");
        var parameters = new object?[]
        {
            contract, cedent, "Facultative", "Property", title, "DN-TEST-UMR-2026", gross,
            10m, 50m, avoirAmount, baseAmount, baseAmount - avoirAmount, 3,
            branding, File.ReadAllBytes(logoPath), bankAccounts
        };

        return (byte[])renderer.Invoke(null, parameters)!;
    }

    private static string FindWorkspaceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "IRISBackend", "Assets", "Branding", "iris-logo.png")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the IRIS backend logo asset.");
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
