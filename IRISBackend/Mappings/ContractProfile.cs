using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;

namespace IRISBackend.Mappings
{
    public sealed class ContractProfile : Profile
    {
        public ContractProfile()
        {
            CreateMap<Currency, CurrencyReferenceDto>()
                .ForMember(destination => destination.Id, options => options.MapFrom(source => source.IdCurrency))
                .ForMember(destination => destination.Code, options => options.MapFrom(source => source.CurrencyCode))
                .ForMember(destination => destination.Symbol, options => options.MapFrom(source => source.CurrencySymbol));

            CreateMap<Counterparty, CedentLookupItemDto>();
            CreateMap<Counterparty, CedentSummaryDto>();

            CreateMap<Contract, FacultativeContractResponseDto>()
                .ForMember(destination => destination.Id, options => options.MapFrom(source => source.IdContract))
                .ForMember(destination => destination.ContractType, options => options.MapFrom(source => source.TypeOfBusiness))
                .ForMember(destination => destination.CounterpartyId, options => options.MapFrom(source => source.IdCounterparty))
                .ForMember(destination => destination.CurrencyId, options => options.MapFrom(source => source.IdCurrency))
                .ForMember(destination => destination.WrittenSharePct, options => options.MapFrom(source => source.WrittenSharePctCedent))
                .ForMember(destination => destination.SignedSharePct, options => options.MapFrom(source => source.SignedSharePctCedent))
                .ForMember(destination => destination.IsHighPriority, options => options.MapFrom(source => source.Flag))
                .ForMember(destination => destination.CedentCommissionPct, options => options.MapFrom(source => source.CedentCommission))
                .ForMember(destination => destination.Cedent, options => options.MapFrom(source => source.IdCounterpartyNavigation))
                .ForMember(destination => destination.Currency, options => options.MapFrom(source => source.IdCurrencyNavigation))
                .ForMember(destination => destination.SumInsured, options => options.MapFrom(source => source.ContractFac == null ? null : source.ContractFac.SumInsured))
                .ForMember(destination => destination.LimitOfLiability, options => options.MapFrom(source => source.ContractFac == null ? null : source.ContractFac.LimitOfLiability))
                .ForMember(destination => destination.Deductible, options => options.MapFrom(source => source.ContractFac == null ? null : source.ContractFac.Deductible))
                .ForMember(destination => destination.PremiumRate, options => options.MapFrom(source => source.ContractFac == null ? null : source.ContractFac.PremiumRate))
                .ForMember(destination => destination.CoverageSelection, options => options.MapFrom(source => MapCoverageSelection(source)));
        }

        private static ContractCoverageSelectionResponseDto? MapCoverageSelection(Contract contract)
        {
            if (contract.ContractCoverageSelections.Count == 0)
            {
                return null;
            }

            var selections = contract.ContractCoverageSelections
                .Where(selection => selection.IdCoverageItemNavigation is not null)
                .ToList();

            var mainCovers = selections
                .Where(selection => string.Equals(selection.IdCoverageItemNavigation.CoverageLevel?.Trim(), CoverageConstants.MainCoverLevel, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(selection => selection.IsPrimary)
                .ThenBy(selection => selection.IdCoverageItem)
                .ToArray();
            var primaryMainCover = mainCovers.FirstOrDefault();

            var sections = selections
                .Where(selection => string.Equals(selection.IdCoverageItemNavigation.CoverageLevel?.Trim(), CoverageConstants.SectionLevel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(selection => selection.IdCoverageItemNavigation.DisplayOrder ?? int.MaxValue)
                .ThenBy(selection => selection.IdCoverageItemNavigation.CoverageName)
                .Select(MapSelectionItem)
                .ToArray();

            var extensions = selections
                .Where(selection => string.Equals(selection.IdCoverageItemNavigation.CoverageLevel?.Trim(), CoverageConstants.ExtensionLevel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(selection => selection.IdCoverageItemNavigation.DisplayOrder ?? int.MaxValue)
                .ThenBy(selection => selection.IdCoverageItemNavigation.CoverageName)
                .Select(MapSelectionItem)
                .ToArray();

            return new ContractCoverageSelectionResponseDto
            {
                PrimaryMainCoverId = primaryMainCover?.IdCoverageItem ?? 0,
                CoverageIds = mainCovers.Select(selection => selection.IdCoverageItem).ToArray(),
                Coverages = mainCovers.Select((selection, index) => new ContractMainCoverSelectionItemDto
                {
                    IdMainCover = selection.IdCoverageItem,
                    Code = selection.IdCoverageItemNavigation.CoverageCode,
                    Description = selection.IdCoverageItemNavigation.Description ?? selection.IdCoverageItemNavigation.CoverageName,
                    IsPrimary = selection.IsPrimary || index == 0,
                    HasSlip = selection.IdSlipTemplate.HasValue,
                    SlipTemplateId = selection.IdSlipTemplate
                }).ToArray(),
                MainCoverId = primaryMainCover?.IdCoverageItem ?? 0,
                SectionIds = sections.Select(section => section.Id).ToArray(),
                ExtensionIds = extensions.Select(extension => extension.Id).ToArray(),
                MainCover = primaryMainCover is null ? null : MapSelectionItem(primaryMainCover),
                Sections = sections,
                Extensions = extensions
            };
        }

        private static ContractCoverageSelectionItemDto MapSelectionItem(ContractCoverageSelection selection)
        {
            return new ContractCoverageSelectionItemDto
            {
                Id = selection.IdCoverageItem,
                Code = selection.IdCoverageItemNavigation.CoverageCode,
                Name = selection.IdCoverageItemNavigation.CoverageName,
                HasSlip = selection.IdSlipTemplate.HasValue,
                SlipTemplateId = selection.IdSlipTemplate
            };
        }
    }
}
