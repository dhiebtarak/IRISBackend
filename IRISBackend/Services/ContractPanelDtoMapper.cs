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
                IdCounterparty = panel.IdCounterparty,
                ReinsurerName = panel.IdCounterpartyNavigation?.Name ?? string.Empty,
                Role = panel.Role ?? string.Empty,
                WrittenSharePct = panel.WrittenSharePct,
                SignedSharePct = panel.SignedSharePct,
                Tsi = panel.Tsi,
                Deductible = panel.Deductible,
                PremiumRatePct = panel.PremiumRatePct,
                GrossPremium = panel.GrossPremium,
                BrokeragePct = panel.BrokeragePct,
                OtherDeductions = panel.OtherDeductions,
                NetPremium = panel.NetPremium,
                PremiumReference = panel.PremiumReference,
                Status = string.IsNullOrWhiteSpace(panel.Status) ? ContractConstants.DraftStatus : panel.Status
            };
        }
    }
}
