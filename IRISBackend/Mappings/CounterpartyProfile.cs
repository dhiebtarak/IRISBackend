using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Mappings
{
    public class CounterpartyProfile : Profile
    {
        public CounterpartyProfile()
        {
            CreateMap<CounterpartyCreateDto, Counterparty>();
            CreateMap<CounterpartyUpdateDto, Counterparty>();
            CreateMap<Counterparty, CounterpartyDetailsDto>();
            CreateMap<Counterparty, CounterpartyListItemDto>();
        }
    }
}
