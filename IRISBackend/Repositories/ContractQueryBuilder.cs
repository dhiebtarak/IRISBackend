using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;

namespace IRISBackend.Repositories
{
    public static class ContractQueryBuilder
    {
        public static IQueryable<Contract> ApplyFilters(IQueryable<Contract> contracts, ContractListQueryDto query)
        {
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                contracts = contracts.Where(contract =>
                    contract.Name.Contains(search) ||
                    (contract.Umr != null && contract.Umr.Contains(search)) ||
                    contract.IdCounterpartyNavigation.Name.Contains(search));
            }

            var contractType = ContractTypeNames.NormalizeFilter(query.ContractType);
            contracts = contractType switch
            {
                ContractTypeNames.Facultative => contracts.Where(contract => contract.ContractFac != null),
                ContractTypeNames.TreatyProportionalQuotaShare => contracts.Where(contract =>
                    contract.ContractTreatyProp != null &&
                    contract.ContractTreatyProp.ContractTreatyPropQuotaShare != null),
                ContractTypeNames.TreatyProportionalSurplus => contracts.Where(contract =>
                    contract.ContractTreatyProp != null &&
                    contract.ContractTreatyProp.ContractTreatyPropSurplu != null),
                ContractTypeNames.TreatyNonProportional => contracts.Where(contract => contract.ContractTreatyNonprops.Any()),
                _ => contracts
            };

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var statuses = ContractConstants.StatusAliases(query.Status.Trim());
                contracts = contracts.Where(contract => statuses.Contains(contract.Status));
            }

            if (query.CounterpartyId.HasValue)
            {
                contracts = contracts.Where(contract => contract.IdCounterparty == query.CounterpartyId.Value);
            }

            if (query.CurrencyId.HasValue)
            {
                contracts = contracts.Where(contract => contract.IdCurrency == query.CurrencyId.Value);
            }

            if (query.InceptionFrom.HasValue)
            {
                contracts = contracts.Where(contract => contract.InceptionDate >= query.InceptionFrom.Value);
            }

            if (query.InceptionTo.HasValue)
            {
                contracts = contracts.Where(contract => contract.InceptionDate <= query.InceptionTo.Value);
            }

            if (query.ExpiryFrom.HasValue)
            {
                contracts = contracts.Where(contract => contract.ExpiryDate >= query.ExpiryFrom.Value);
            }

            if (query.ExpiryTo.HasValue)
            {
                contracts = contracts.Where(contract => contract.ExpiryDate <= query.ExpiryTo.Value);
            }

            return contracts;
        }

        public static IQueryable<ContractListItemDto> ApplySorting(IQueryable<ContractListItemDto> contracts, ContractListQueryDto query)
        {
            var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            var sortBy = query.SortBy.Trim().ToLowerInvariant();

            return sortBy switch
            {
                "name" => descending ? contracts.OrderByDescending(contract => contract.Name) : contracts.OrderBy(contract => contract.Name),
                "umr" => descending ? contracts.OrderByDescending(contract => contract.Umr) : contracts.OrderBy(contract => contract.Umr),
                "cedentname" => descending ? contracts.OrderByDescending(contract => contract.CedentName) : contracts.OrderBy(contract => contract.CedentName),
                "contracttype" => descending ? contracts.OrderByDescending(contract => contract.ContractType) : contracts.OrderBy(contract => contract.ContractType),
                "status" => descending ? contracts.OrderByDescending(contract => contract.Status) : contracts.OrderBy(contract => contract.Status),
                "receptiondate" => descending ? contracts.OrderByDescending(contract => contract.ReceptionDate) : contracts.OrderBy(contract => contract.ReceptionDate),
                "inceptiondate" => descending ? contracts.OrderByDescending(contract => contract.InceptionDate) : contracts.OrderBy(contract => contract.InceptionDate),
                "expirydate" => descending ? contracts.OrderByDescending(contract => contract.ExpiryDate) : contracts.OrderBy(contract => contract.ExpiryDate),
                "grosspremium" => descending ? contracts.OrderByDescending(contract => contract.GrossPremium) : contracts.OrderBy(contract => contract.GrossPremium),
                "createdat" => descending ? contracts.OrderByDescending(contract => contract.CreatedAt) : contracts.OrderBy(contract => contract.CreatedAt),
                _ => descending
                    ? contracts.OrderByDescending(contract => contract.UpdatedAt ?? contract.CreatedAt)
                    : contracts.OrderBy(contract => contract.UpdatedAt ?? contract.CreatedAt)
            };
        }

        public static IQueryable<ContractListItemDto> ProjectListItems(IQueryable<Contract> contracts)
        {
            return contracts.Select(contract => new ContractListItemDto
            {
                IdContract = contract.IdContract,
                Umr = contract.Umr,
                Name = contract.Name,
                CedentId = contract.IdCounterparty,
                CedentName = contract.IdCounterpartyNavigation.Name,
                ContractType = contract.ContractFac != null
                    ? ContractTypeNames.Facultative
                    : contract.ContractTreatyProp != null && contract.ContractTreatyProp.ContractTreatyPropQuotaShare != null
                        ? ContractTypeNames.TreatyProportionalQuotaShare
                        : contract.ContractTreatyProp != null && contract.ContractTreatyProp.ContractTreatyPropSurplu != null
                            ? ContractTypeNames.TreatyProportionalSurplus
                            : contract.ContractTreatyNonprops.Any()
                                ? ContractTypeNames.TreatyNonProportional
                                : null,
                TypeOfBusiness = contract.TypeOfBusiness,
                CurrencyId = contract.IdCurrency,
                CurrencyCode = contract.IdCurrencyNavigation.CurrencyCode,
                Status = contract.Status,
                ReceptionDate = contract.ReceptionDate,
                InceptionDate = contract.InceptionDate,
                ExpiryDate = contract.ExpiryDate,
                GrossPremium = contract.GrossPremium,
                ActionRequired = contract.Flag,
                CreatedAt = contract.CreatedAt,
                UpdatedAt = contract.UpdatedAt
            });
        }

        public static IQueryable<RecentContractDto> ProjectRecentContracts(IQueryable<Contract> contracts)
        {
            return contracts.Select(contract => new RecentContractDto
            {
                IdContract = contract.IdContract,
                Umr = contract.Umr,
                Name = contract.Name,
                CedentId = contract.IdCounterparty,
                CedentName = contract.IdCounterpartyNavigation.Name,
                ContractType = contract.ContractFac != null
                    ? ContractTypeNames.Facultative
                    : contract.ContractTreatyProp != null && contract.ContractTreatyProp.ContractTreatyPropQuotaShare != null
                        ? ContractTypeNames.TreatyProportionalQuotaShare
                        : contract.ContractTreatyProp != null && contract.ContractTreatyProp.ContractTreatyPropSurplu != null
                            ? ContractTypeNames.TreatyProportionalSurplus
                            : contract.ContractTreatyNonprops.Any()
                                ? ContractTypeNames.TreatyNonProportional
                                : null,
                Status = contract.Status,
                CurrencyCode = contract.IdCurrencyNavigation.CurrencyCode,
                InceptionDate = contract.InceptionDate,
                ExpiryDate = contract.ExpiryDate,
                UpdatedAt = contract.UpdatedAt ?? contract.CreatedAt
            });
        }
    }
}
