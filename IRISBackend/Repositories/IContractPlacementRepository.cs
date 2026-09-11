using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories
{
    public interface IContractPlacementRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        Task<Contract?> GetContractForPlacementAsync(
            int contractId,
            string userId,
            CancellationToken cancellationToken = default);

        Task<ContractPanel?> GetPanelAsync(
            int contractId,
            int panelId,
            string userId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ContractPanelResponseDto>> GetPanelsAsync(
            int contractId,
            string userId,
            CancellationToken cancellationToken = default);

        Task<bool> IsActiveReinsurerAsync(
            int counterpartyId,
            CancellationToken cancellationToken = default);

        Task<bool> ContractHasReinsurerPanelAsync(
            int contractId,
            int counterpartyId,
            int? excludedPanelId = null,
            CancellationToken cancellationToken = default);

        Task AddPanelAsync(ContractPanel panel, CancellationToken cancellationToken = default);

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
