using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests
{
    public sealed class ContractQueryBuilderTests
    {
        [Theory]
        [InlineData("Property", 1)]
        [InlineData("FAC-UMR-002", 2)]
        [InlineData("AMI", 2)]
        public void ApplyFilters_SearchesNameUmrAndCedent(string search, int expectedContractId)
        {
            var result = ContractQueryBuilder.ApplyFilters(SeedContracts(), new ContractListQueryDto { Search = search }).ToList();

            Assert.Single(result);
            Assert.Equal(expectedContractId, result[0].IdContract);
        }

        [Fact]
        public void ApplyFilters_FiltersByDerivedContractType()
        {
            var result = ContractQueryBuilder.ApplyFilters(SeedContracts(), new ContractListQueryDto
            {
                ContractType = "FACULTATIVE"
            }).ToList();

            Assert.Equal([1, 2], result.Select(contract => contract.IdContract).OrderBy(id => id));
        }

        [Fact]
        public void ApplyFilters_FiltersByStatusAndDateRanges()
        {
            var result = ContractQueryBuilder.ApplyFilters(SeedContracts(), new ContractListQueryDto
            {
                Status = "Draft",
                InceptionFrom = new DateOnly(2026, 2, 1),
                InceptionTo = new DateOnly(2026, 12, 31),
                ExpiryFrom = new DateOnly(2027, 1, 1),
                ExpiryTo = new DateOnly(2027, 12, 31)
            }).ToList();

            Assert.Single(result);
            Assert.Equal(2, result[0].IdContract);
        }

        [Fact]
        public void ApplySorting_SupportsServerSidePagination()
        {
            var query = new ContractListQueryDto
            {
                PageNumber = 2,
                PageSize = 1,
                SortBy = "name",
                SortDirection = "asc"
            };

            var page = ContractQueryBuilder.ApplySorting(ContractQueryBuilder.ProjectListItems(SeedContracts()), query)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            Assert.Single(page);
            Assert.Equal("Property Facultative", page[0].Name);
        }

        [Fact]
        public void ProjectListItems_UsesReceptionDateAndPreservesNull()
        {
            var result = ContractQueryBuilder.ProjectListItems(SeedContracts()).ToList();

            Assert.Equal(new DateOnly(2026, 8, 20), result.Single(item => item.IdContract == 1).ReceptionDate);
            Assert.Null(result.Single(item => item.IdContract == 2).ReceptionDate);
        }

        [Fact]
        public void ApplySorting_SupportsReceptionDate()
        {
            var result = ContractQueryBuilder.ApplySorting(
                ContractQueryBuilder.ProjectListItems(SeedContracts()),
                new ContractListQueryDto { SortBy = "receptionDate", SortDirection = "desc" })
                .ToList();

            Assert.Equal(1, result[0].IdContract);
        }

        [Fact]
        public void ApplyFilters_WhenNothingMatches_ReturnsEmptyResult()
        {
            var result = ContractQueryBuilder.ApplyFilters(SeedContracts(), new ContractListQueryDto
            {
                Search = "does-not-exist"
            }).ToList();

            Assert.Empty(result);
        }

        private static IQueryable<Contract> SeedContracts()
        {
            var atlas = new Counterparty { Id = 30, Name = "Atlas Cedent" };
            var ami = new Counterparty { Id = 31, Name = "AMI" };
            var currency = new Currency { IdCurrency = 1, CurrencyCode = "TND" };

            return new List<Contract>
            {
                CreateContract(1, "Property Facultative", "FAC-UMR-001", "Draft", atlas, currency,
                    new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), receptionDate: new DateOnly(2026, 8, 20), facultative: true),
                CreateContract(2, "Marine Facultative", "FAC-UMR-002", "Draft", ami, currency,
                    new DateOnly(2026, 2, 1), new DateOnly(2027, 2, 1), facultative: true),
                CreateContract(3, "Quota Contract", "QS-003", "Submitted", atlas, currency,
                    new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), quotaShare: true)
            }.AsQueryable();
        }

        private static Contract CreateContract(
            int id,
            string name,
            string umr,
            string status,
            Counterparty cedent,
            Currency currency,
            DateOnly inception,
            DateOnly expiry,
            DateOnly? receptionDate = null,
            bool facultative = false,
            bool quotaShare = false)
        {
            var contract = new Contract
            {
                IdContract = id,
                Name = name,
                Umr = umr,
                Status = status,
                IdCounterparty = cedent.Id,
                IdCounterpartyNavigation = cedent,
                IdCurrency = currency.IdCurrency,
                IdCurrencyNavigation = currency,
                TypeOfBusiness = "Property",
                ReceptionDate = receptionDate,
                InceptionDate = inception,
                ExpiryDate = expiry,
                CreatedAt = new DateTime(2026, 1, id, 0, 0, 0, DateTimeKind.Utc),
                IsCurrent = true,
                IdUser = "user-1"
            };

            if (facultative)
            {
                contract.ContractFac = new ContractFac { IdContract = id };
            }

            if (quotaShare)
            {
                contract.ContractTreatyProp = new ContractTreatyProp
                {
                    IdContract = id,
                    ContractTreatyPropQuotaShare = new ContractTreatyPropQuotaShare()
                };
            }

            return contract;
        }
    }
}
