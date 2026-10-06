using IRISBackend.Models;

namespace IRISBackend.Services;

public static class PremiumDebitNoteFinancials
{
    public static decimal ResolveContractGrossPremium(Contract contract)
    {
        var sumInsured = contract.ContractFac?.SumInsured;
        var premiumRate = contract.ContractFac?.PremiumRate;
        if (sumInsured.HasValue && premiumRate.HasValue)
        {
            return decimal.Round(sumInsured.Value * premiumRate.Value / 100m, 4, MidpointRounding.AwayFromZero);
        }

        return contract.GrossPremium ?? 0m;
    }
}
