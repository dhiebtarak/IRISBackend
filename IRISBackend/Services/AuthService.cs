using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.AspNetCore.Identity;

namespace IRISBackend.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ITokenService _tokenService;
        private readonly ILoginAuditService _loginAuditService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ITokenService tokenService,
            ILoginAuditService loginAuditService,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _tokenService = tokenService;
            _loginAuditService = loginAuditService;
            _logger = logger;
        }

        public async Task<ServiceResult<UserProfileResponseDto>> CreateUserAsync(RegisterRequestDto request)
        {
            var ruleErrors = RegistrationRules.Validate(request).ToArray();
            if (ruleErrors.Length > 0)
            {
                return ServiceResult<UserProfileResponseDto>.Failure("User validation failed.", ruleErrors);
            }

            if (await _userManager.FindByNameAsync(request.Username.Trim()) is not null)
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "Username already exists.", ["Username: This username is already in use."],
                    StatusCodes.Status409Conflict, "DUPLICATE_USERNAME");
            }

            if (await _userManager.FindByEmailAsync(request.Email.Trim()) is not null)
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "Email already exists.", ["Email: This email address is already in use."],
                    StatusCodes.Status409Conflict, "DUPLICATE_EMAIL");

            if (!await _roleManager.RoleExistsAsync(IdentityRoles.Employee))
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "The staff role is not configured.", statusCode: StatusCodes.Status409Conflict,
                    code: "STAFF_ROLE_NOT_CONFIGURED");

            var (department, position) = RegistrationRules.Normalize(request.Department, request.Position);
            var user = new ApplicationUser
            {
                UserName = request.Username.Trim(),
                Email = request.Email.Trim(),
                FullName = request.FullName.Trim(),
                Department = department,
                Position = position
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(error => error.Description).ToArray();
                var duplicateUserName = result.Errors.Any(error => error.Code.Contains("UserName", StringComparison.OrdinalIgnoreCase));
                var duplicateEmail = result.Errors.Any(error => error.Code.Contains("Email", StringComparison.OrdinalIgnoreCase));
                var status = duplicateUserName || duplicateEmail ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
                var code = duplicateUserName ? "DUPLICATE_USERNAME" : duplicateEmail ? "DUPLICATE_EMAIL" : "USER_CREATION_FAILED";
                return ServiceResult<UserProfileResponseDto>.Failure("User creation failed.", errors, status, code);
            }

            IdentityResult roleResult;
            try
            {
                roleResult = await _userManager.AddToRoleAsync(user, IdentityRoles.Employee);
            }
            catch (Exception exception)
            {
                var cleanup = await _userManager.DeleteAsync(user);
                if (!cleanup.Succeeded)
                    _logger.LogCritical(exception, "Failed to assign required staff role and failed to remove the newly created account. UserId={UserId}", user.Id);
                else
                    _logger.LogError(exception, "Failed to assign required staff role; the newly created account was removed. UserId={UserId}", user.Id);
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "The account could not be assigned its required staff role.", statusCode: StatusCodes.Status500InternalServerError,
                    code: "STAFF_ROLE_ASSIGNMENT_FAILED");
            }

            if (!roleResult.Succeeded)
            {
                var cleanup = await _userManager.DeleteAsync(user);
                if (!cleanup.Succeeded)
                    _logger.LogCritical("Failed to assign required staff role and failed to remove the newly created account. UserId={UserId}", user.Id);
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "The account could not be assigned its required staff role.",
                    roleResult.Errors.Select(error => error.Description),
                    StatusCodes.Status500InternalServerError, "STAFF_ROLE_ASSIGNMENT_FAILED");
            }

            return ServiceResult<UserProfileResponseDto>.Success(
                UserMapping.ToProfile(user, [IdentityRoles.Employee]), StatusCodes.Status201Created);
        }

        public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto request)
        {
            var user = await _userManager.FindByNameAsync(request.EmailOrUsername)
                ?? await _userManager.FindByEmailAsync(request.EmailOrUsername);

            if (user is null)
            {
                await _loginAuditService.RecordAsync(null, request.EmailOrUsername, false, "User was not found.");
                return InvalidCredentials();
            }

            var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!signInResult.Succeeded)
            {
                await _loginAuditService.RecordAsync(user.Id, request.EmailOrUsername, false, GetFailureReason(signInResult));
                return InvalidCredentials();
            }

            await _loginAuditService.RecordAsync(user.Id, request.EmailOrUsername, true);

            var response = await BuildAuthResponseAsync(user);
            return ServiceResult<AuthResponseDto>.Success(response);
        }

        public Task LogoutAsync()
        {
            return _signInManager.SignOutAsync();
        }

        private async Task<AuthResponseDto> BuildAuthResponseAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var profile = UserMapping.ToProfile(user, roles);
            var token = await _tokenService.CreateTokenAsync(user);

            return new AuthResponseDto
            {
                Id = profile.Id,
                FullName = profile.FullName,
                Username = profile.Username,
                Email = profile.Email,
                Department = profile.Department,
                Position = profile.Position,
                Roles = profile.Roles,
                Token = token,
                User = profile
            };
        }

        private static ServiceResult<AuthResponseDto> InvalidCredentials()
        {
            return ServiceResult<AuthResponseDto>.Failure(
                "Invalid email/username or password.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        private static string GetFailureReason(SignInResult signInResult)
        {
            if (signInResult.IsLockedOut)
            {
                return "User is locked out.";
            }

            if (signInResult.IsNotAllowed)
            {
                return "User is not allowed to sign in.";
            }

            if (signInResult.RequiresTwoFactor)
            {
                return "Two-factor authentication is required.";
            }

            return "Invalid password.";
        }
    }
}
