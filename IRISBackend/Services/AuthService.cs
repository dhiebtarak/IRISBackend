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

        public AuthService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ITokenService tokenService,
            ILoginAuditService loginAuditService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _tokenService = tokenService;
            _loginAuditService = loginAuditService;
        }

        public async Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request)
        {
            var ruleErrors = RegistrationRules.Validate(request).ToArray();
            if (ruleErrors.Length > 0)
            {
                return ServiceResult<AuthResponseDto>.Failure("Validation failed.", ruleErrors);
            }

            if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            {
                return ServiceResult<AuthResponseDto>.Failure(
                    "Validation failed.",
                    ["Password and confirm password do not match."]);
            }

            var user = new ApplicationUser
            {
                UserName = request.Username,
                Email = request.Email,
                FullName = request.FullName,
                Department = request.Department,
                Position = request.Position
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return ServiceResult<AuthResponseDto>.Failure(
                    "Registration failed.",
                    result.Errors.Select(error => error.Description));
            }

            if (!await _roleManager.RoleExistsAsync(IdentityRoles.Employee))
            {
                await _roleManager.CreateAsync(new IdentityRole(IdentityRoles.Employee));
            }

            var roleResult = await _userManager.AddToRoleAsync(user, IdentityRoles.Employee);
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);

                return ServiceResult<AuthResponseDto>.Failure(
                    "Registration failed.",
                    roleResult.Errors.Select(error => error.Description));
            }

            var response = await BuildAuthResponseAsync(user);
            return ServiceResult<AuthResponseDto>.Success(response, StatusCodes.Status201Created);
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
