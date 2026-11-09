using FluentValidation;
using IRISBackend.DTOs;
namespace IRISBackend.Validators;
public sealed class AccessActivityQueryValidator : AbstractValidator<AccessActivityQuery>
{
    public static readonly string[] Events = ["LoginSuccessful", "LoginFailed", "Logout", "SessionExpired", "SessionRevoked", "AccountLocked", "AccountUnlocked"];
    public AccessActivityQueryValidator()
    {
        RuleFor(q => q.PageNumber).InclusiveBetween(1, 1000000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(256);
        RuleFor(q => q.UserId).MaximumLength(450);
        RuleFor(q => q.Role).MaximumLength(100);
        RuleFor(q => q.Department).MaximumLength(100);
        RuleFor(q => q.SessionStatus).Must(v => v == null || new[] {"Active","LoggedOut","Expired","Revoked"}.Contains(v));
        RuleFor(q => q.AccountStatus).Must(v => v == null || new[] {"Enabled","Disabled","Locked"}.Contains(v));
        RuleFor(q => q.EventType).Must(v => v == null || Events.Contains(v));
        RuleFor(q => q.Result).Must(v => v == null || new[] {"Successful","Failed"}.Contains(v));
        RuleFor(q => q.SortBy).Must(v => new[] {"loginAtUtc","lastActivityAtUtc","username","sessionStatus"}.Contains(v));
        RuleFor(q => q.SortDirection).Must(v => v is "asc" or "desc");
        RuleFor(q => q.DateTo).GreaterThanOrEqualTo(q => q.DateFrom).When(q => q.DateFrom.HasValue && q.DateTo.HasValue);
        RuleFor(q => q.DateFrom).Must(v => v == null || v.Value.Kind == DateTimeKind.Utc).WithMessage("Use a UTC date with a Z suffix.");
        RuleFor(q => q.DateTo).Must(v => v == null || v.Value.Kind == DateTimeKind.Utc).WithMessage("Use a UTC date with a Z suffix.");
    }
}
public sealed class RevokeSessionRequestValidator : AbstractValidator<RevokeSessionRequest>
{
    public RevokeSessionRequestValidator() { RuleFor(r => r.Reason).NotEmpty().MaximumLength(200).Must(v => v != null && !v.Any(char.IsControl)); }
}
