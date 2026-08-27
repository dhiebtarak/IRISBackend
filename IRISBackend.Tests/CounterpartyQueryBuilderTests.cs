using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using Xunit;

namespace IRISBackend.Tests
{
    public sealed class CounterpartyQueryBuilderTests
    {
        [Fact]
        public void ApplyFilters_SearchesExistingFieldsAndAppliesCountryCityAndStatus()
        {
            var counterparties = SeedCounterparties();
            var result = CounterpartyQueryBuilder.ApplyFilters(counterparties, new CounterpartyQueryDto
            {
                Search = "AXA-UK",
                Type = "Cedent",
                Status = "Active",
                Country = "United Kingdom",
                City = "London"
            }).ToList();

            Assert.Single(result);
            Assert.Equal(1, result[0].Id);
        }

        [Fact]
        public void ApplySorting_UsesAllowlistedFieldAndDirection()
        {
            var result = CounterpartyQueryBuilder.ApplySorting(SeedCounterparties(), new CounterpartyQueryDto
            {
                SortBy = "city",
                SortDirection = "desc"
            }).ToList();

            Assert.Equal(["Tunis", "London"], result.Select(item => item.City));
        }

        [Fact]
        public void Pagination_ReturnsRequestedCounterpartyPage()
        {
            var query = new CounterpartyQueryDto { PageNumber = 2, PageSize = 1 };
            var result = CounterpartyQueryBuilder.ApplySorting(SeedCounterparties(), query)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            Assert.Single(result);
            Assert.Equal("AXA XL", result[0].Name);
        }

        private static IQueryable<Counterparty> SeedCounterparties()
        {
            return new List<Counterparty>
            {
                new()
                {
                    Id = 1,
                    Name = "AXA XL",
                    LegacyCompanyCode = "AXA-UK",
                    Type = "Cedent",
                    Status = "Active",
                    IsActive = true,
                    Country = "United Kingdom",
                    City = "London"
                },
                new()
                {
                    Id = 2,
                    Name = "AMI",
                    LegacyCompanyCode = "AMI-TN",
                    Type = "Cedent",
                    Status = "Active",
                    IsActive = true,
                    Country = "Tunisie",
                    City = "Tunis"
                }
            }.AsQueryable();
        }
    }
}
