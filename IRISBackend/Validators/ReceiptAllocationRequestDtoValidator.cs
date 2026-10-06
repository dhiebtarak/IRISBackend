using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators;

public sealed class ReceiptAllocationRequestDtoValidator : AbstractValidator<ReceiptAllocationRequestDto>
{
    public ReceiptAllocationRequestDtoValidator()
    {
        RuleFor(request => request.ContractId)
            .GreaterThan(0)
            .WithMessage("contractId must be a positive numeric Contract ID.");

        RuleFor(request => request.AllocatedAmount)
            .GreaterThan(0m)
            .WithMessage("allocatedAmount must be greater than zero.");
    }
}
