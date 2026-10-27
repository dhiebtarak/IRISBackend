using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories
{
    public sealed class ContractPlacementRepository : IContractPlacementRepository
    {
        private readonly IRISBackendContext _dbContext;

        public ContractPlacementRepository(IRISBackendContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        public Task<IDbContextTransaction> BeginSerializableTransactionAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);
        }

        public Task<Contract?> GetContractForPlacementAsync(
            int contractId,
            string userId,
            bool includeAllContracts = false,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.Contracts
                .Include(contract => contract.ContractPanels)
                    .ThenInclude(panel => panel.IdCounterpartyNavigation)
                .Include(contract => contract.IdCurrencyNavigation)
                .FirstOrDefaultAsync(contract =>
                    contract.IdContract == contractId &&
                    contract.IsCurrent &&
                    (includeAllContracts || contract.IdUser == userId),
                    cancellationToken);
        }

        public Task<ContractPanel?> GetPanelAsync(
            int contractId,
            int panelId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.ContractPanels
                .Include(panel => panel.IdContractNavigation)
                    .ThenInclude(contract => contract.IdCurrencyNavigation)
                .Include(panel => panel.IdCounterpartyNavigation)
                .FirstOrDefaultAsync(panel =>
                    panel.IdPanel == panelId &&
                    panel.IdContract == contractId &&
                    panel.IdContractNavigation.IsCurrent &&
                    panel.IdContractNavigation.IdUser == userId,
                    cancellationToken);
        }

        public async Task<IReadOnlyList<ContractPanelResponseDto>> GetPanelsAsync(
            int contractId,
            string userId,
            bool includeAllContracts = false,
            CancellationToken cancellationToken = default)
        {
            var panels = await _dbContext.ContractPanels
                .AsNoTracking()
                .Where(panel =>
                    panel.IdContract == contractId &&
                    panel.IdContractNavigation.IsCurrent &&
                    (includeAllContracts || panel.IdContractNavigation.IdUser == userId))
                .OrderBy(panel => panel.IdPanel)
                .Select(panel => new ContractPanelResponseDto
                {
                    IdPanel = panel.IdPanel,
                    IdContract = panel.IdContract,
                    ContractGrossPremium = panel.IdContractNavigation.GrossPremium,
                    ContractSignedSharePercentage = panel.IdContractNavigation.SignedSharePctCedent,
                    ContractNetPremium = panel.IdContractNavigation.NetPremiumCedent,
                    ContractCedentCommissionPercentage = panel.IdContractNavigation.CedentCommission,
                    CurrencyDecimalPlaces = panel.IdContractNavigation.IdCurrencyNavigation.DecimalPlaces,
                    IdCounterparty = panel.IdCounterparty,
                    ReinsurerName = panel.IdCounterpartyNavigation.Name,
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
                    Status = string.IsNullOrWhiteSpace(panel.Status)
                        ? ContractConstants.DraftStatus
                        : panel.Status
                })
                .ToListAsync(cancellationToken);

            foreach (var panel in panels)
            {
                panel.ContractCedentCommissionAmount = panel.ContractGrossPremium.HasValue
                    ? PremiumDebitNoteFinancials.CalculateCedentCommissionAmount(
                        panel.ContractGrossPremium.Value,
                        panel.ContractCedentCommissionPercentage,
                        panel.CurrencyDecimalPlaces)
                    : null;
                panel.ContractNetPremium = panel.ContractGrossPremium.HasValue
                    ? PremiumCalculationService.CalculateContractNetPremium(
                        panel.ContractGrossPremium.Value,
                        panel.ContractCedentCommissionPercentage,
                        panel.CurrencyDecimalPlaces)
                    : null;
                Console.WriteLine(
                    $"Panel {panel.IdPanel}: Tsi={panel.Tsi}, PremiumRatePct={panel.PremiumRatePct}, BrokeragePct={panel.BrokeragePct}, PremiumReference={panel.PremiumReference}");
            }

            return panels;
        }

        public Task<bool> IsActiveReinsurerAsync(
            int counterpartyId,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.Counterparties
                .AsNoTracking()
                .AnyAsync(counterparty =>
                    counterparty.Id == counterpartyId &&
                    counterparty.IsActive &&
                    counterparty.TypeAssignments.Any(assignment => assignment.Type == CounterpartyTypes.Reinsurer),
                    cancellationToken);
        }

        public Task<bool> ContractHasReinsurerPanelAsync(
            int contractId,
            int counterpartyId,
            int? excludedPanelId = null,
            CancellationToken cancellationToken = default)
        {
            return _dbContext.ContractPanels
                .AsNoTracking()
                .AnyAsync(panel =>
                    panel.IdContract == contractId &&
                    panel.IdCounterparty == counterpartyId &&
                    (!excludedPanelId.HasValue || panel.IdPanel != excludedPanelId.Value),
                    cancellationToken);
        }

        public Task AddPanelAsync(ContractPanel panel, CancellationToken cancellationToken = default)
        {
            return _dbContext.ContractPanels.AddAsync(panel, cancellationToken).AsTask();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
