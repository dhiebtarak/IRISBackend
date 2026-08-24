using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Models;

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
                .ForMember(destination => destination.PremiumRate, options => options.MapFrom(source => source.ContractFac == null ? null : source.ContractFac.PremiumRate));
        }
    }
}
