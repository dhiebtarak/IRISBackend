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

            var mainCover = selections.SingleOrDefault(selection =>
                string.Equals(selection.IdCoverageItemNavigation.CoverageLevel?.Trim(), CoverageConstants.MainCoverLevel, StringComparison.OrdinalIgnoreCase));

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
                MainCoverId = mainCover?.IdCoverageItem ?? 0,
                SectionIds = sections.Select(section => section.Id).ToArray(),
                ExtensionIds = extensions.Select(extension => extension.Id).ToArray(),
                MainCover = mainCover is null ? null : MapSelectionItem(mainCover),
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
