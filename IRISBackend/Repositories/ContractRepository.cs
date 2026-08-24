using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IRISBackend.Repositories
{
    public sealed class ContractRepository : IContractRepository
    {
        private readonly IRISBackendContext _dbContext;

        public ContractRepository(IRISBackendContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        public Task<bool> UmrExistsAsync(string umr, int? excludedContractId = null, CancellationToken cancellationToken = default)
        {
            var normalizedUmr = umr.Trim().ToUpperInvariant();

            return _dbContext.Contracts.AnyAsync(
                contract =>
                    contract.Umr != null &&
                    contract.Umr.ToUpper() == normalizedUmr &&
                    (!excludedContractId.HasValue || contract.IdContract != excludedContractId.Value),
                cancellationToken);
        }

        public Task AddAsync(Contract contract, CancellationToken cancellationToken = default)
        {
            return _dbContext.Contracts.AddAsync(contract, cancellationToken).AsTask();
        }

        public Task AddFacultativeDetailsAsync(ContractFac contractFac, CancellationToken cancellationToken = default)
        {
            return _dbContext.ContractFacs.AddAsync(contractFac, cancellationToken).AsTask();
        }

        public void Update(Contract contract)
        {
            _dbContext.Contracts.Update(contract);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task<Contract?> GetFacultativeContractByIdAsync(
            int contractId,
            bool asNoTracking = false,
            CancellationToken cancellationToken = default)
        {
            IQueryable<Contract> query = _dbContext.Contracts
                .Include(contract => contract.ContractFac)
                .Include(contract => contract.IdCounterpartyNavigation)
                .Include(contract => contract.IdCurrencyNavigation)
                .Where(contract =>
                    contract.IdContract == contractId &&
                    contract.TypeOfBusiness == ContractConstants.FacultativeType);

            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return query.SingleOrDefaultAsync(cancellationToken);
        }
    }
}
