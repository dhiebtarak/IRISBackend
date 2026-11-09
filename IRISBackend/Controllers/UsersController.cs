using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/users")]
    public class UsersController : ApiControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAdminUserManagementService _adminUserManagementService;
        private readonly IAuthService _authService;

        public UsersController(IUserService userService, IAdminUserManagementService adminUserManagementService, IAuthService authService)
        {
            _userService = userService;
            _adminUserManagementService = adminUserManagementService;
            _authService = authService;
        }

        [HttpGet("me")]
        [ProducesResponseType(typeof(UserProfileResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Me()
        {
            var result = await _userService.GetCurrentUserAsync(User);
            return FromServiceResult(result);
        }

        [HttpPost]
        [Authorize(Roles = IdentityRoles.Admin)]
        [ProducesResponseType(typeof(UserProfileResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> CreateUser(RegisterRequestDto request)
        {
            var result = await _authService.CreateUserAsync(request);
            return FromServiceResult(result);
        }

        [HttpGet("admin")]
        [Authorize(Roles = IdentityRoles.Admin)]
        public async Task<IActionResult> ListUsers([FromQuery] string? search) =>
            FromServiceResult(await _adminUserManagementService.ListUsersForAdminAsync(search));

        [HttpGet("admin/{userId}/modules")]
        [Authorize(Roles = IdentityRoles.Admin)]
        public async Task<IActionResult> GetModules(string userId) =>
            FromServiceResult(await _adminUserManagementService.GetUserModulesForAdminAsync(userId));

        [HttpPut("admin/{userId}/modules")]
        [Authorize(Roles = IdentityRoles.Admin)]
        public async Task<IActionResult> SetModules(string userId, [FromBody] UpdateUserModulesRequestDto request) =>
            FromServiceResult(await _adminUserManagementService.SetUserModulesForAdminAsync(userId, request.Modules));

        [HttpPatch("admin/{userId}/status")]
        [Authorize(Roles = IdentityRoles.Admin)]
        public async Task<IActionResult> SetAccountStatus(string userId, [FromBody] UpdateUserAccountStatusRequestDto request) =>
            FromServiceResult(await _adminUserManagementService.SetUserEnabledForAdminAsync(userId, request.IsEnabled, User));
    }
}
