using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Services
{
    internal static class ContractPanelDtoMapper
    {
        public static ContractPanelResponseDto Map(ContractPanel panel)
        {
            return new ContractPanelResponseDto
            {
                IdPanel = panel.IdPanel,
                IdContract = panel.IdContract,
                ContractGrossPremium = panel.IdContractNavigation?.GrossPremium,
                ContractSignedSharePercentage = panel.IdContractNavigation?.SignedSharePctCedent,
                ContractNetPremium = panel.IdContractNavigation?.GrossPremium is decimal contractGross
                    ? PremiumCalculationService.CalculateContractNetPremium(
                        contractGross,
                        panel.IdContractNavigation.CedentCommission,
                        panel.IdContractNavigation.IdCurrencyNavigation?.DecimalPlaces)
                    : null,
                ContractCedentCommissionPercentage = panel.IdContractNavigation?.CedentCommission,
                ContractCedentCommissionAmount = panel.IdContractNavigation is { } contract && contract.GrossPremium.HasValue
                    ? PremiumCalculationService.CalculateCedentCommissionAmount(
                        contract.GrossPremium.Value,
                        contract.CedentCommission,
                        contract.IdCurrencyNavigation?.DecimalPlaces)
                    : null,
                IdCounterparty = panel.IdCounterparty,
                ReinsurerName = panel.IdCounterpartyNavigation?.Name ?? string.Empty,
                Role = panel.Role ?? string.Empty,
                WrittenSharePct = panel.WrittenSharePct,
                SignedSharePct = panel.SignedSharePct,
                Tsi = panel.Tsi,
                SumInsured = panel.Tsi,
                Deductible = panel.Deductible,
                LimitOfLiabilityType = panel.LimitOfLiabilityType,
                LimitOfLiabilityValue = panel.LimitOfLiabilityValue,
                LimitOfLiabilityAmount = panel.LimitOfLiabilityAmount,
                DeductibleType = panel.DeductibleType,
                DeductibleValue = panel.DeductibleValue,
                DeductibleAmount = panel.DeductibleAmount ?? panel.Deductible,
                PremiumRatePct = panel.PremiumRatePct,
                GrossPremium = panel.GrossPremium,
                BrokeragePct = panel.BrokeragePct,
                BrokerageIrisPct = panel.BrokeragePct,
                CedentCommissionPct = panel.CedentCommissionPct,
                CedentCommissionAmount = panel.CedentCommissionAmount,
                BrokerageIrisAmount = panel.BrokerageIrisAmount,
                OtherDeductions = panel.OtherDeductions,
                OtherDeductionsPercentage = panel.OtherDeductionsPercentage,
                OtherDeductionsAmount = panel.OtherDeductions,
                NetPremium = panel.NetPremium,
                PremiumReference = panel.PremiumReference,
                Status = string.IsNullOrWhiteSpace(panel.Status) ? ContractConstants.DraftStatus : panel.Status
            };
        }
    }
}
