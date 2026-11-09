using FluentValidation;
using IRISBackend.DTOs;
namespace IRISBackend.Validators;

public sealed class BankReceiptRequestValidator : AbstractValidator<BankReceiptRequestDto>
{
    public BankReceiptRequestValidator()
    {
        RuleFor(x => x.OperationDate).NotNull().WithMessage("Encaissement date is required; use YYYY-MM-DD.")
            .Must(d => !d.HasValue || d.Value != default).WithMessage("Select a valid complete encaissement date.");
        RuleFor(x => x.BankAccountId).GreaterThan(0);
        RuleFor(x => x.CounterpartyId).GreaterThan(0).When(x => x.CounterpartyId.HasValue);
        RuleFor(x => x.AmountReceived).GreaterThan(0);
        RuleFor(x => x.SettlementMethod).NotEmpty().MaximumLength(255);
        RuleFor(x => x.BankReference).MaximumLength(100);
        RuleFor(x => x.BusinessReference).MaximumLength(255);
        RuleFor(x => x.SettlementLabel).MaximumLength(255);
        RuleFor(x => x.Reason).MaximumLength(255);
    }
}
public sealed class FinancialReceiptQueryValidator : AbstractValidator<FinancialReceiptQueryDto>
{
    public FinancialReceiptQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(255);
        RuleFor(x => x.CounterpartyId).GreaterThan(0).When(x => x.CounterpartyId.HasValue);
        RuleFor(x => x.CurrencyId).GreaterThan(0).When(x => x.CurrencyId.HasValue);
        RuleFor(x => x.OperationFrom).Must(d => !d.HasValue || d.Value != default);
        RuleFor(x => x.OperationTo).Must(d => !d.HasValue || d.Value != default);
        RuleFor(x => x.OperationTo).GreaterThanOrEqualTo(x => x.OperationFrom).When(x => x.OperationFrom.HasValue && x.OperationTo.HasValue).WithMessage("Operation date from must not be after date to.");
        RuleFor(x => x.MinAmount).GreaterThanOrEqualTo(0).When(x => x.MinAmount.HasValue);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(0).When(x => x.MaxAmount.HasValue);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(x => x.MinAmount).When(x => x.MinAmount.HasValue && x.MaxAmount.HasValue).WithMessage("Minimum amount must not exceed maximum amount.");
        RuleFor(x => x.PairingStatus).Must(v => string.IsNullOrEmpty(v) || new[] { "unpaired", "partiallypaired", "paired" }.Contains(v.Replace("_", "").Replace(" ", "").ToLowerInvariant()));
        RuleFor(x => x.DetailsStatus).Must(v => string.IsNullOrEmpty(v) || new[] { "unpaired", "partiallypaired", "paired" }.Contains(v.Replace("_", "").Replace(" ", "").ToLowerInvariant()));
        RuleFor(x => x.SortBy).Must(v => string.IsNullOrEmpty(v) || new[] { "operationdate", "receptiondate", "amountreceived", "pairingstatus", "counterpartyname" }.Contains(v.ToLowerInvariant()));
        RuleFor(x => x.SortDirection).Must(v => string.IsNullOrEmpty(v) || v.ToLowerInvariant() is "asc" or "desc");
    }
}
