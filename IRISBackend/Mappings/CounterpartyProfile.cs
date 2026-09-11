using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Mappings
{
    public class CounterpartyProfile : Profile
    {
        public CounterpartyProfile()
        {
            CreateMap<CounterpartyCreateDto, Counterparty>()
                .ForMember(destination => destination.Type, options => options.Ignore())
                .ForMember(destination => destination.TypeAssignments, options => options.Ignore());
            CreateMap<CounterpartyUpdateDto, Counterparty>()
                .ForMember(destination => destination.Type, options => options.Ignore())
                .ForMember(destination => destination.TypeAssignments, options => options.Ignore());
            CreateMap<Counterparty, CounterpartyDetailsDto>()
                .ForMember(destination => destination.Types, options => options.MapFrom(source => source.TypeAssignments.OrderBy(assignment => assignment.Type).Select(assignment => assignment.Type).ToArray()));
            CreateMap<Counterparty, CounterpartyListItemDto>()
                .ForMember(destination => destination.Types, options => options.MapFrom(source => source.TypeAssignments.OrderBy(assignment => assignment.Type).Select(assignment => assignment.Type).ToArray()));
        }
    }
}
