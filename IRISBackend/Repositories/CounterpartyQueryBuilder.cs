using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Repositories
{
    public static class CounterpartyQueryBuilder
    {
        public static IQueryable<Counterparty> ApplyFilters(IQueryable<Counterparty> counterparties, CounterpartyQueryDto query)
        {
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                counterparties = counterparties.Where(counterparty =>
                    (counterparty.LegacyClientId.HasValue && counterparty.LegacyClientId.Value.ToString().Contains(search)) ||
                    (counterparty.LegacyCompanyCode != null && counterparty.LegacyCompanyCode.Contains(search)) ||
                    counterparty.Name.Contains(search) ||
                    (counterparty.Email != null && counterparty.Email.Contains(search)) ||
                    (counterparty.ContactName != null && counterparty.ContactName.Contains(search)) ||
                    (counterparty.Country != null && counterparty.Country.Contains(search)) ||
                    (counterparty.City != null && counterparty.City.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(query.Type))
            {
                var type = query.Type.Trim();
                counterparties = counterparties.Where(counterparty => counterparty.Type == type);
            }

            if (query.IsActive.HasValue)
            {
                counterparties = counterparties.Where(counterparty => counterparty.IsActive == query.IsActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var status = query.Status.Trim();
                counterparties = counterparties.Where(counterparty => counterparty.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query.Country))
            {
                var country = query.Country.Trim();
                counterparties = counterparties.Where(counterparty => counterparty.Country == country);
            }

            if (!string.IsNullOrWhiteSpace(query.City))
            {
                var city = query.City.Trim();
                counterparties = counterparties.Where(counterparty => counterparty.City == city);
            }

            return counterparties;
        }

        public static IQueryable<Counterparty> ApplySorting(IQueryable<Counterparty> counterparties, CounterpartyQueryDto query)
        {
            var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

            return query.SortBy.Trim().ToLowerInvariant() switch
            {
                "type" => descending ? counterparties.OrderByDescending(item => item.Type) : counterparties.OrderBy(item => item.Type),
                "email" => descending ? counterparties.OrderByDescending(item => item.Email) : counterparties.OrderBy(item => item.Email),
                "status" => descending ? counterparties.OrderByDescending(item => item.Status) : counterparties.OrderBy(item => item.Status),
                "country" => descending ? counterparties.OrderByDescending(item => item.Country) : counterparties.OrderBy(item => item.Country),
                "city" => descending ? counterparties.OrderByDescending(item => item.City) : counterparties.OrderBy(item => item.City),
                "legacycompanycode" => descending
                    ? counterparties.OrderByDescending(item => item.LegacyCompanyCode)
                    : counterparties.OrderBy(item => item.LegacyCompanyCode),
                "createdat" => descending ? counterparties.OrderByDescending(item => item.CreatedAtUtc) : counterparties.OrderBy(item => item.CreatedAtUtc),
                "updatedat" => descending
                    ? counterparties.OrderByDescending(item => item.UpdatedAtUtc ?? item.CreatedAtUtc)
                    : counterparties.OrderBy(item => item.UpdatedAtUtc ?? item.CreatedAtUtc),
                _ => descending ? counterparties.OrderByDescending(item => item.Name) : counterparties.OrderBy(item => item.Name)
            };
        }

        public static IQueryable<CounterpartyListItemDto> ProjectListItems(IQueryable<Counterparty> counterparties)
        {
            return counterparties.Select(counterparty => new CounterpartyListItemDto
            {
                Id = counterparty.Id,
                LegacyClientId = counterparty.LegacyClientId,
                LegacyCompanyCode = counterparty.LegacyCompanyCode,
                Name = counterparty.Name,
                Type = counterparty.Type,
                Email = counterparty.Email,
                Country = counterparty.Country,
                City = counterparty.City,
                Status = counterparty.Status,
                IsActive = counterparty.IsActive
            });
        }
    }
}
