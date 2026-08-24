using IRISBackend.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories
{
    public interface IContractRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default);

        Task AddAsync(Contract contract, CancellationToken cancellationToken = default);

        Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default);

        void Update(Contract contract);

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        Task<Contract?> GetFacultativeContractByIdAsync(
            int contractId,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default);
    }
}
