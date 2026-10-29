using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace IRISBackend.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ApiControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public AuthController(
            IAuthService authService,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _authService = authService;
            _configuration = configuration;
            _environment = environment;
        }

        [HttpPost("register")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register(RegisterRequestDto request)
        {
            var allowPublicRegistration = _configuration.GetValue<bool?>("Registration:AllowPublicRegistration")
                ?? _environment.IsDevelopment();
            if (!allowPublicRegistration)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponseDto
                {
                    Code = "PUBLIC_REGISTRATION_DISABLED",
                    Message = "Public registration is disabled. Contact an administrator for an account.",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

            var result = await _authService.RegisterAsync(request);
            return FromServiceResult(result);
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponseDto), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login(LoginRequestDto request)
        {
            var result = await _authService.LoginAsync(request);
            return FromServiceResult(result);
        }

        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            await _authService.LogoutAsync();
            return Ok(new { message = "Logged out." });
        }
    }
}
