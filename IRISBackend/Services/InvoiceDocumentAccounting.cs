using IRISBackend.Models;

namespace IRISBackend.Services;

internal static class InvoiceDocumentAccounting
{
    public const string Invoice = "INVOICE";
    public const string Fees = "INVOICE_FEES";
    public const string Avoir = "AVOIR";
    public const string PairWithEncaissement = "PAIR_WITH_ENCAISSEMENT";
    public const string DirectToRemittances = "DIRECT_TO_REMITTANCES";

    public static string NormalizeProcessingMode(string? mode) => mode?.Trim().ToUpperInvariant() ?? string.Empty;

    public static bool IsAvoir(string? type) =>
        string.Equals(type?.Trim(), Avoir, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type?.Trim(), "CREDIT_NOTE", StringComparison.OrdinalIgnoreCase);

    public static bool IsContractReceivableType(string? type) =>
        NormalizeStoredType(type) is Invoice or Fees or Avoir;

    public static decimal SignedAmount(Invoice invoice)
    {
        var amount = invoice.GrossPremium ?? 0m;
        return IsAvoir(invoice.InvoiceType) ? -amount : amount;
    }

    public static bool IsActiveDocument(Invoice invoice) =>
        !string.IsNullOrWhiteSpace(invoice.Status)
        && !string.Equals(invoice.Status.Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(invoice.Status.Trim(), "Archived", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(invoice.Status.Trim(), "Invalid", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(invoice.Status.Trim(), "Void", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(invoice.Status.Trim(), "Voided", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(invoice.Status.Trim(), "Deleted", StringComparison.OrdinalIgnoreCase)
        && invoice.GrossPremium is > 0m
        && IsContractReceivableType(invoice.InvoiceType);

    public static InvoiceDocumentTotals CalculateTotals(IEnumerable<Invoice> invoices)
    {
        var activeDocuments = invoices.Where(IsActiveDocument).ToList();
        var totalInvoiced = activeDocuments
            .Where(invoice => !IsAvoir(invoice.InvoiceType))
            .Sum(invoice => invoice.GrossPremium ?? 0m);
        var totalAvoir = activeDocuments
            .Where(invoice => IsAvoir(invoice.InvoiceType))
            .Sum(invoice => invoice.GrossPremium ?? 0m);

        return new InvoiceDocumentTotals(
            totalInvoiced,
            totalAvoir);
    }

    public static decimal AvailableAvoirBalance(decimal grossPremium, decimal? cedentCommissionPct, decimal totalAvoirAmount, int? currencyDecimalPlaces = null)
    {
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        return Math.Max(0m, PremiumDebitNoteFinancials.RoundMoney(
            AvoirBase(grossPremium, cedentCommissionPct, precision) - totalAvoirAmount, precision));
    }

    public static decimal AvoirBase(decimal grossPremium, decimal? cedentCommissionPct, int? currencyDecimalPlaces = null)
    {
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        var commissionAmount = PremiumDebitNoteFinancials.CalculateCedentCommissionAmount(
            grossPremium, cedentCommissionPct, precision);
        return PremiumDebitNoteFinancials.RoundMoney(grossPremium - commissionAmount, precision);
    }

    public static decimal NetInvoiced(IEnumerable<Invoice> invoices) => invoices
        .Where(IsActiveDocument)
        .Sum(SignedAmount);

    public static string NormalizeType(string? type) => type?.Trim().ToUpperInvariant() ?? string.Empty;

    public static string NormalizeStoredType(string? type) => NormalizeType(type) switch
    {
        "CEDENT_PREMIUM_INVOICE" => Invoice,
        "CEDENT_PREMIUM_FEES" or "INVOICE_FEE" or "INVOICE FEES" => Fees,
        "CREDIT_NOTE" => Avoir,
        var normalized => normalized
    };
}

internal sealed record InvoiceDocumentTotals(decimal TotalInvoicedAmount, decimal TotalAvoirAmount);
