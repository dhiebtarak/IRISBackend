using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface ICoverageSelectionService
    {
        Task<ServiceResult<CoverageSelectionPlan>> ValidateAsync(
            CoverageSelectionRequestDto? request,
            CancellationToken cancellationToken = default);

        Task CreateSelectionsAsync(
            int contractId,
            CoverageSelectionPlan plan,
            string userId,
            DateTime now,
            CancellationToken cancellationToken = default);

        Task ReplaceSelectionsAsync(
            int contractId,
            CoverageSelectionPlan plan,
            string userId,
            DateTime now,
            CancellationToken cancellationToken = default);
    }
}
