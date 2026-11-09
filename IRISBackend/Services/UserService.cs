using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace IRISBackend.Services
{
    public class UserService : IUserService, IAdminUserManagementService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppIdentityContext _identityDb;
        private readonly IRISBackendContext? _activityDb;
        private readonly ILoginAuditService? _audit;

        public UserService(UserManager<ApplicationUser> userManager, AppIdentityContext identityDb, IRISBackendContext? activityDb = null, ILoginAuditService? audit = null)
        {
            _userManager = userManager;
            _identityDb = identityDb; _activityDb = activityDb; _audit = audit;
        }

        public async Task<ServiceResult<UserProfileResponseDto>> GetCurrentUserAsync(ClaimsPrincipal principal)
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User identity was not found in the token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var roles = await _userManager.GetRolesAsync(user);
            var profile = UserMapping.ToProfile(user, roles);
            profile.Modules = await GetEffectiveModulesAsync(user);
            profile.IsEnabled = !await _userManager.IsLockedOutAsync(user);
            profile.ModuleAssignmentsConfigured = (await _userManager.GetClaimsAsync(user))
                .Any(claim => claim.Type == ModuleAccessCatalog.ConfigurationClaimType);
            return ServiceResult<UserProfileResponseDto>.Success(profile);
        }

        public async Task<IReadOnlyList<string>> GetEffectiveModulesAsync(ApplicationUser user)
        {
            if (await _userManager.IsInRoleAsync(user, IdentityRoles.Admin)) return [];
            var claims = await _userManager.GetClaimsAsync(user);
            if (claims.Any(claim => claim.Type == ModuleAccessCatalog.ConfigurationClaimType))
            {
                return claims.Where(claim => claim.Type == ModuleAccessCatalog.ClaimType)
                    .Select(claim => claim.Value)
                    .Where(ModuleAccessCatalog.IsValid)
                    .Select(ModuleAccessCatalog.Canonicalize)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return ModuleAccessCatalog.LegacyModules(user.Department, user.Position);
        }

        public async Task<ServiceResult<IReadOnlyList<AdminUserListItemDto>>> ListUsersForAdminAsync(string? search)
        {
            var users = await _userManager.Users.AsNoTracking().OrderBy(user => user.FullName).ToListAsync();
            var normalized = search?.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                users = users.Where(user =>
                    (user.FullName ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (user.UserName ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (user.Email ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (user.Department ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    (user.Position ?? string.Empty).Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var now = DateTime.UtcNow;
            var sessionMetrics = _activityDb == null ? new Dictionary<string, (DateTime?, int)>() :
                (await _activityDb.AuthSessions.AsNoTracking().GroupBy(s => s.UserId)
                    .Select(g => new { UserId = g.Key, Last = g.Max(s => s.LoginAtUtc), Active = g.Count(s => s.Status == "Active" && s.ExpiresAtUtc > now) })
                    .ToListAsync()).ToDictionary(m => m.UserId, m => ((DateTime?)m.Last, m.Active));
            var lastLogins = _activityDb == null ? new Dictionary<string, DateTime>() :
                await _activityDb.UserLoginAudits.AsNoTracking().Where(a => a.Succeeded && a.EventType == "LoginSuccessful" && a.UserId != null)
                    .GroupBy(a => a.UserId).Select(g => new { UserId = g.Key, Last = g.Max(a => a.CreatedAtUtc) })
                    .ToDictionaryAsync(m => m.UserId, m => m.Last);
            var rows = new List<AdminUserListItemDto>(users.Count);
            foreach (var user in users)
            {
                rows.Add(new AdminUserListItemDto
                {
                    LastSuccessfulLoginAtUtc = lastLogins.TryGetValue(user.Id, out var lastLogin) ? lastLogin : sessionMetrics.GetValueOrDefault(user.Id).Item1,
                    ActiveSessionCount = sessionMetrics.GetValueOrDefault(user.Id).Item2,
                    IsLocked = user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow,
                    AccountStatus = user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow ? (user.LockoutEnd > DateTimeOffset.UtcNow.AddYears(10) ? "Disabled" : "Locked") : "Enabled",
                    Id = user.Id,
                    FullName = user.FullName,
                    Username = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    Department = user.Department,
                    Position = user.Position,
                    Roles = (await _userManager.GetRolesAsync(user)).ToArray(),
                    Modules = await GetEffectiveModulesAsync(user),
                    IsEnabled = !await _userManager.IsLockedOutAsync(user)
                });
            }

            return ServiceResult<IReadOnlyList<AdminUserListItemDto>>.Success(rows);
        }

        public async Task<ServiceResult<IReadOnlyList<string>>> GetUserModulesForAdminAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return ServiceResult<IReadOnlyList<string>>.Failure("User was not found.", statusCode: StatusCodes.Status404NotFound);
            if (await _userManager.IsInRoleAsync(user, IdentityRoles.Admin))
                return ServiceResult<IReadOnlyList<string>>.Failure("Administrator access is managed by the Admin role, not module grants.", statusCode: StatusCodes.Status409Conflict);
            return ServiceResult<IReadOnlyList<string>>.Success(await GetEffectiveModulesAsync(user));
        }

        public async Task<ServiceResult<IReadOnlyList<string>>> SetUserModulesForAdminAsync(string userId, IReadOnlyCollection<string>? modules)
        {
            if (modules is null) return ServiceResult<IReadOnlyList<string>>.Failure("Modules are required.", ["modules is required."]);
            var invalid = modules.Where(module => !ModuleAccessCatalog.IsValid(module)).Distinct().ToArray();
            if (invalid.Length > 0) return ServiceResult<IReadOnlyList<string>>.Failure("One or more modules are unsupported.", invalid.Select(module => $"Unsupported module: {module}"));

            var canonical = modules.Select(ModuleAccessCatalog.Canonicalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return ServiceResult<IReadOnlyList<string>>.Failure("User was not found.", statusCode: StatusCodes.Status404NotFound);
            if (await _userManager.IsInRoleAsync(user, IdentityRoles.Admin))
                return ServiceResult<IReadOnlyList<string>>.Failure("Administrator access is managed by the Admin role, not module grants.", statusCode: StatusCodes.Status409Conflict);

            await using var transaction = await _identityDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = await _userManager.GetClaimsAsync(user);
            var oldModuleClaims = current.Where(claim => claim.Type is ModuleAccessCatalog.ClaimType or ModuleAccessCatalog.ConfigurationClaimType).ToArray();
            var remove = await _userManager.RemoveClaimsAsync(user, oldModuleClaims);
            if (!remove.Succeeded)
            {
                await transaction.RollbackAsync();
                return ServiceResult<IReadOnlyList<string>>.Failure("Could not replace module assignments.", remove.Errors.Select(error => error.Description), StatusCodes.Status409Conflict);
            }

            var replacement = canonical.Select(module => new System.Security.Claims.Claim(ModuleAccessCatalog.ClaimType, module))
                .Append(new System.Security.Claims.Claim(ModuleAccessCatalog.ConfigurationClaimType, "true"))
                .ToArray();
            var add = await _userManager.AddClaimsAsync(user, replacement);
            if (!add.Succeeded)
            {
                await transaction.RollbackAsync();
                return ServiceResult<IReadOnlyList<string>>.Failure("Could not save module assignments.", add.Errors.Select(error => error.Description), StatusCodes.Status409Conflict);
            }

            await transaction.CommitAsync();
            return ServiceResult<IReadOnlyList<string>>.Success(canonical);
        }

        public async Task<ServiceResult<UserAccountStatusResponseDto>> SetUserEnabledForAdminAsync(string userId, bool isEnabled, ClaimsPrincipal actor)
        {
            await using var transaction = await _identityDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                await transaction.RollbackAsync();
                return ServiceResult<UserAccountStatusResponseDto>.Failure("User was not found.", statusCode: StatusCodes.Status404NotFound);
            }
            var actorId = actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? actor.FindFirstValue("sub");
            if (!isEnabled && string.Equals(actorId, user.Id, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync();
                return ServiceResult<UserAccountStatusResponseDto>.Failure("You cannot disable your own administrator account.", statusCode: StatusCodes.Status409Conflict);
            }

            if (!isEnabled && await _userManager.IsInRoleAsync(user, IdentityRoles.Admin))
            {
                var admins = await _userManager.GetUsersInRoleAsync(IdentityRoles.Admin);
                var enabledAdmins = 0;
                foreach (var admin in admins)
                    if (!await _userManager.IsLockedOutAsync(admin)) enabledAdmins++;
                if (enabledAdmins <= 1)
                {
                    await transaction.RollbackAsync();
                    return ServiceResult<UserAccountStatusResponseDto>.Failure("The last enabled administrator cannot be disabled.", statusCode: StatusCodes.Status409Conflict);
                }
            }

            var lockoutEnabled = await _userManager.SetLockoutEnabledAsync(user, true);
            if (!lockoutEnabled.Succeeded)
            {
                await transaction.RollbackAsync();
                return ServiceResult<UserAccountStatusResponseDto>.Failure("Could not update account status.", lockoutEnabled.Errors.Select(error => error.Description), StatusCodes.Status409Conflict);
            }
            var lockout = await _userManager.SetLockoutEndDateAsync(user, isEnabled ? null : DateTimeOffset.MaxValue);
            if (!lockout.Succeeded)
            {
                await transaction.RollbackAsync();
                return ServiceResult<UserAccountStatusResponseDto>.Failure("Could not update account status.", lockout.Errors.Select(error => error.Description), StatusCodes.Status409Conflict);
            }

            await transaction.CommitAsync();
            if (_audit != null) await _audit.RecordEventAsync(user.Id, user.UserName ?? "", isEnabled ? "AccountUnlocked" : "AccountLocked", actorUserId: actorId);
            return ServiceResult<UserAccountStatusResponseDto>.Success(new UserAccountStatusResponseDto { UserId = user.Id, IsEnabled = isEnabled });
        }
    }
}
