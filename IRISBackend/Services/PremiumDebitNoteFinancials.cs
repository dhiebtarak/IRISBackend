using IRISBackend.Models;

namespace IRISBackend.Services;

public sealed record SignedInvoiceablePremium(
    decimal ContractGrossPremium,
    decimal CedentCommissionPercentage,
    decimal CedentCommissionAmount,
    decimal ContractSignedSharePercentage,
    decimal SignedGrossPremium,
    decimal SignedCedentCommissionAmount,
    decimal InvoiceableNetPremium,
    int CurrencyDecimalPlaces);

public sealed record InvoiceablePremium(
    decimal ContractGrossPremium,
    decimal CedentCommissionPercentage,
    decimal CedentCommissionAmount,
    decimal InvoiceableNetPremium,
    int CurrencyDecimalPlaces);

public static class PremiumDebitNoteFinancials
{
    public static decimal ResolveContractGrossPremium(Contract contract)
    {
        if (contract.GrossPremium.HasValue)
        {
            return contract.GrossPremium.Value;
        }

        var sumInsured = contract.ContractFac?.SumInsured;
        var premiumRate = contract.ContractFac?.PremiumRate;
        if (sumInsured.HasValue && premiumRate.HasValue)
        {
            return FacultativeFinancialCalculator.CalculateGrossPremium(
                sumInsured.Value, premiumRate.Value, contract.IdCurrencyNavigation?.DecimalPlaces);
        }

        return 0m;
    }

    public static decimal CalculateCedentCommissionAmount(decimal grossPremium, decimal? commissionPercentage, int? decimalPlaces) =>
        PremiumCalculationService.CalculateCedentCommissionAmount(grossPremium, commissionPercentage, decimalPlaces);

    public static decimal CalculateInvoiceableNetPremium(Contract contract, decimal grossPremium, int? decimalPlaces = null)
    {
        return CalculateInvoiceableNetPremium(grossPremium, contract.CedentCommission,
            decimalPlaces ?? contract.IdCurrencyNavigation?.DecimalPlaces);
    }

    public static decimal CalculateInvoiceableNetPremium(decimal grossPremium, decimal? cedentCommissionPercentage, int? decimalPlaces)
    {
        return PremiumCalculationService.CalculateContractNetPremium(
            grossPremium, cedentCommissionPercentage, decimalPlaces);
    }

    public static bool TryCalculateSignedInvoiceablePremium(
        Contract contract,
        out SignedInvoiceablePremium? result,
        out string? validationError)
    {
        result = null;
        validationError = null;
        var precision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);

        if (contract.ContractFac is not null)
        {
            if (contract.ContractFac.SumInsured is < 0m)
                return Invalid("sumInsured", "Sum insured cannot be negative.", out validationError);
            if (contract.ContractFac.PremiumRate is null or < 0m or > 100m)
                return Invalid("premiumRate", "Premium rate must be between 0 and 100 percent.", out validationError);
        }

        var commissionPercentage = contract.CedentCommission ?? 0m;
        if (commissionPercentage is < 0m or > 100m)
            return Invalid("cedentCommissionPercentage", "Cedent commission percentage must be between 0 and 100.", out validationError);

        var signedSharePercentage = contract.SignedSharePctCedent;
        if (signedSharePercentage is null or <= 0m or > 100m)
            return Invalid("contractSignedSharePercentage", "Contract signed share percentage must be greater than 0 and no greater than 100.", out validationError);

        var grossPremium = ResolveContractGrossPremium(contract);
        if (grossPremium < 0m)
            return Invalid("contractGrossPremium", "Contract gross premium cannot be negative.", out validationError);

        var commissionAmount = CalculateCedentCommissionAmount(grossPremium, commissionPercentage, precision);
        if (commissionAmount > grossPremium)
            return Invalid("cedentCommissionPercentage", "Cedent commission amount cannot exceed contract gross premium.", out validationError);

        var signedGrossPremium = RoundMoney(grossPremium * signedSharePercentage.Value / 100m, precision);
        var contractNetPremium = RoundMoney(grossPremium - commissionAmount, precision);
        var invoiceableNetPremium = RoundMoney(contractNetPremium * signedSharePercentage.Value / 100m, precision);
        var signedCommissionAmount = signedGrossPremium - invoiceableNetPremium;
        if (invoiceableNetPremium < 0m)
            return Invalid("invoiceableNetPremium", "Calculated invoiceable net premium cannot be negative.", out validationError);

        result = new SignedInvoiceablePremium(
            grossPremium,
            commissionPercentage,
            commissionAmount,
            signedSharePercentage.Value,
            signedGrossPremium,
            signedCommissionAmount,
            invoiceableNetPremium,
            precision);
        return true;
    }

    public static bool TryCalculateInvoiceablePremium(
        Contract contract,
        out InvoiceablePremium? result,
        out string? validationError)
    {
        result = null;
        validationError = null;
        var precision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);

        if (contract.ContractFac is not null)
        {
            if (contract.ContractFac.SumInsured is < 0m)
                return Invalid("sumInsured", "Sum insured cannot be negative.", out validationError);
            if (contract.ContractFac.PremiumRate is < 0m or > 100m)
                return Invalid("premiumRate", "Premium rate must be between 0 and 100 percent.", out validationError);
        }

        var commissionPercentage = contract.CedentCommission ?? 0m;
        if (commissionPercentage is < 0m or > 100m)
            return Invalid("cedentCommissionPercentage", "Cedent commission percentage must be between 0 and 100.", out validationError);

        var grossPremium = ResolveContractGrossPremium(contract);
        if (grossPremium < 0m)
            return Invalid("contractGrossPremium", "Contract gross premium cannot be negative.", out validationError);

        var commissionAmount = CalculateCedentCommissionAmount(grossPremium, commissionPercentage, precision);
        if (commissionAmount > grossPremium)
            return Invalid("cedentCommissionPercentage", "Cedent commission amount cannot exceed contract gross premium.", out validationError);

        var invoiceableNetPremium = RoundMoney(grossPremium - commissionAmount, precision);
        result = new InvoiceablePremium(
            grossPremium,
            commissionPercentage,
            commissionAmount,
            invoiceableNetPremium,
            precision);
        return true;
    }

    private static bool Invalid(string field, string message, out string? error)
    {
        error = $"{field}: {message}";
        return false;
    }

    public static decimal RoundMoney(decimal amount, int precision) =>
        decimal.Round(amount, precision, MidpointRounding.AwayFromZero);
}
