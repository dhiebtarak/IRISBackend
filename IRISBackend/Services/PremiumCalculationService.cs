using IRISBackend.Models;

namespace IRISBackend.Services;

public sealed record ContractPremiumValues(
    decimal GrossPremium,
    decimal CedentCommissionPercentage,
    decimal CedentCommissionAmount,
    decimal NetPremium,
    decimal SignedSharePercentage);

public sealed class PremiumCalculationService
{
    public static decimal CalculateCedentCommissionAmount(decimal grossPremium, decimal? percentage, int? currencyDecimalPlaces)
    {
        var effectivePercentage = percentage ?? 0m;
        if (grossPremium < 0m)
            throw new PremiumCalculationValidationException(["grossPremium must be greater than or equal to zero."]);
        if (effectivePercentage is < 0m or > 100m)
            throw new PremiumCalculationValidationException(["cedentCommission must be between 0 and 100."]);
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        return Round(grossPremium * effectivePercentage / 100m, precision);
    }

    public static decimal CalculateContractNetPremium(decimal grossPremium, decimal? percentage, int? currencyDecimalPlaces)
    {
        var commission = CalculateCedentCommissionAmount(grossPremium, percentage, currencyDecimalPlaces);
        return Round(grossPremium - commission, PanelRemittanceCalculator.Precision(currencyDecimalPlaces));
    }

    public IReadOnlyList<string> ValidateContract(Contract contract)
    {
        var errors = new List<string>();
        if (contract.GrossPremium is null or < 0m)
            errors.Add("grossPremium must be present and greater than or equal to zero.");
        if (contract.CedentCommission is < 0m or > 100m)
            errors.Add("cedentCommission must be between 0 and 100.");
        if (contract.SignedSharePctCedent is null or <= 0m or > 100m)
            errors.Add("signedSharePctCedent must be greater than zero and no more than 100.");
        return errors;
    }

    public ContractPremiumValues CalculateContract(Contract contract, int? currencyDecimalPlaces)
    {
        var errors = ValidateContract(contract);
        if (errors.Count > 0)
            throw new PremiumCalculationValidationException(errors);

        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        var gross = Round(contract.GrossPremium!.Value, precision);
        var commissionPct = contract.CedentCommission ?? 0m;
        var commission = CalculateCedentCommissionAmount(gross, commissionPct, precision);
        return new(gross, commissionPct, commission, CalculateContractNetPremium(gross, commissionPct, precision), contract.SignedSharePctCedent!.Value);
    }

    public IReadOnlyList<string> ValidatePanel(Contract contract, ContractPanel panel)
    {
        var errors = ValidateContract(contract).ToList();
        if (panel.SignedSharePct is null or < 0m or > 100m)
            errors.Add("signedSharePct must be between 0 and 100.");
        if (panel.WrittenSharePct is null or <= 0m or > 100m)
            errors.Add("writtenSharePct must be greater than zero and no more than 100.");
        if (panel.SignedSharePct.HasValue && panel.WrittenSharePct.HasValue && panel.SignedSharePct > panel.WrittenSharePct)
            errors.Add("signedSharePct cannot exceed writtenSharePct.");
        if (panel.BrokeragePct is < 0m or > 100m)
            errors.Add("brokeragePct must be between 0 and 100.");
        if (panel.OtherDeductions is < 0m)
            errors.Add("otherDeductions must be greater than or equal to zero.");
        if (panel.OtherDeductionsPercentage is < 0m or > 100m)
            errors.Add("otherDeductionsPercentage must be between 0 and 100.");
        return errors;
    }

    public IReadOnlyList<string> RecalculatePanels(Contract contract, int? currencyDecimalPlaces)
    {
        var allPanels = contract.ContractPanels
            .OrderBy(panel => panel.IdPanel)
            .ToArray();
        var errors = ValidateContract(contract).ToList();
        if (errors.Count > 0)
            return errors;

        foreach (var panel in allPanels.Where(panel => !IsExcluded(panel.Status) &&
                     string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase) &&
                     (!panel.SignedSharePct.HasValue || !panel.WrittenSharePct.HasValue)))
            errors.Add($"Panel {panel.IdPanel} must have writtenSharePct and signedSharePct before confirmation.");

        var panels = allPanels
            .Where(panel => panel.SignedSharePct.HasValue && panel.WrittenSharePct.HasValue)
            .ToArray();
        var activePanels = panels.Where(panel => !IsExcluded(panel.Status)).ToArray();

        foreach (var panel in panels)
            errors.AddRange(ValidatePanel(contract, panel));

        if (errors.Count > 0)
            return errors.Distinct().ToArray();

        var values = CalculateContract(contract, currencyDecimalPlaces);

        var confirmedShare = activePanels
            .Where(panel => string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase))
            .Sum(panel => panel.SignedSharePct ?? 0m);
        if (confirmedShare > values.SignedSharePercentage)
            errors.Add("Total confirmed panel signed shares cannot exceed the contract signed share.");

        var activeShare = activePanels.Sum(panel => panel.SignedSharePct ?? 0m);
        if (activeShare > values.SignedSharePercentage)
            errors.Add("Total active panel signed shares cannot exceed the contract signed share.");
        if (errors.Count > 0)
            return errors.Distinct().ToArray();

        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        var grossByPanel = panels.Select(panel => Round(
            values.GrossPremium * panel.SignedSharePct!.Value / values.SignedSharePercentage,
            precision)).ToArray();

        // When the panel shares cover the full contract share, assign rounding residue to the final panel.
        if (activePanels.Length > 0 && activeShare == values.SignedSharePercentage)
        {
            var activePanelIds = activePanels.Select(panel => panel.IdPanel).ToHashSet();
            var activeAllocatedTotal = panels.Select((panel, index) => (panel, index))
                .Where(item => activePanelIds.Contains(item.panel.IdPanel))
                .Sum(item => grossByPanel[item.index]);
            var finalActiveIndex = Array.FindLastIndex(panels, panel => activePanelIds.Contains(panel.IdPanel));
            grossByPanel[finalActiveIndex] += values.GrossPremium - activeAllocatedTotal;
        }

        for (var index = 0; index < panels.Length; index++)
        {
            var commissionAmount = Round(grossByPanel[index] * values.CedentCommissionPercentage / 100m, precision);
            var panelNet = grossByPanel[index]
                - commissionAmount
                - Round(grossByPanel[index] * (panels[index].BrokeragePct ?? 0m) / 100m, precision)
                - Round(panels[index].OtherDeductions ?? 0m, precision);
            if (Round(panelNet, precision) < 0m)
                errors.Add($"Calculated panel netPremium cannot be negative for panel {panels[index].IdPanel}.");
        }
        if (errors.Count > 0)
            return errors;

        contract.NetPremiumCedent = values.NetPremium;
        for (var index = 0; index < panels.Length; index++)
            ApplyPanel(contract, panels[index], values, grossByPanel[index], precision);

        return [];
    }

    public ContractPanelResponseCalculation CalculatePanel(
        Contract contract,
        ContractPanel panel,
        int? currencyDecimalPlaces)
    {
        var errors = ValidatePanel(contract, panel);
        if (errors.Count > 0)
            throw new PremiumCalculationValidationException(errors);
        var contractValues = CalculateContract(contract, currencyDecimalPlaces);
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        var gross = Round(contractValues.GrossPremium * panel.SignedSharePct!.Value / contractValues.SignedSharePercentage, precision);
        ApplyPanel(contract, panel, contractValues, gross, precision);
        return new(contractValues, gross, panel.BrokeragePct ?? 0m, panel.BrokerageIrisAmount ?? 0m,
            panel.OtherDeductions ?? 0m, panel.NetPremium ?? 0m);
    }

    private static void ApplyPanel(
        Contract contract,
        ContractPanel panel,
        ContractPremiumValues contractValues,
        decimal gross,
        int precision)
    {
        var brokeragePct = panel.BrokeragePct ?? 0m;
        var brokerageAmount = Round(gross * brokeragePct / 100m, precision);
        var cedentCommissionAmount = Round(gross * contractValues.CedentCommissionPercentage / 100m, precision);
        var deductions = panel.OtherDeductionsPercentage.HasValue
            ? Round(gross * panel.OtherDeductionsPercentage.Value / 100m, precision)
            : Round(panel.OtherDeductions ?? 0m, precision);
        var net = Round(gross - cedentCommissionAmount - brokerageAmount - deductions, precision);

        panel.CedentCommissionPct = contractValues.CedentCommissionPercentage;
        panel.CedentCommissionAmount = cedentCommissionAmount;
        panel.BrokerageIrisAmount = brokerageAmount;
        panel.OtherDeductions = deductions;
        panel.GrossPremium = gross;
        panel.NetPremium = net;

        if (!string.IsNullOrWhiteSpace(panel.LimitOfLiabilityType) && panel.LimitOfLiabilityValue.HasValue)
        {
            panel.LimitOfLiabilityAmount = string.Equals(panel.LimitOfLiabilityType, "Percentage", StringComparison.OrdinalIgnoreCase)
                ? Round((panel.Tsi ?? 0m) * panel.LimitOfLiabilityValue.Value / 100m, precision)
                : Round(panel.LimitOfLiabilityValue.Value, precision);
        }

        if (!string.IsNullOrWhiteSpace(panel.DeductibleType) && panel.DeductibleValue.HasValue)
        {
            panel.DeductibleAmount = string.Equals(panel.DeductibleType, "Percentage", StringComparison.OrdinalIgnoreCase)
                ? Round(gross * panel.DeductibleValue.Value / 100m, precision)
                : Round(panel.DeductibleValue.Value, precision);
            panel.Deductible = panel.DeductibleAmount;
        }
    }

    public static decimal Round(decimal value, int precision) =>
        decimal.Round(value, precision, MidpointRounding.AwayFromZero);

    public static bool IsExcluded(string? status) =>
        string.Equals(status?.Trim(), ContractConstants.CancelledPanelStatus, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), ContractConstants.NotTakenUpPanelStatus, StringComparison.OrdinalIgnoreCase);
}

public sealed record ContractPanelResponseCalculation(
    ContractPremiumValues Contract,
    decimal PanelGrossPremium,
    decimal BrokeragePercentage,
    decimal BrokerageAmount,
    decimal OtherDeductionsAmount,
    decimal PanelNetPremium);

public sealed class PremiumCalculationValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
