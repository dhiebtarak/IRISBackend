using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace IRISBackend.Controllers
{
    public abstract class ApiControllerBase : ControllerBase
    {
        protected IActionResult FromServiceResult<T>(ServiceResult<T> result)
        {
            if (result.Succeeded)
            {
                return StatusCode(result.StatusCode, result.Value);
            }

            return result.StatusCode switch
            {
                StatusCodes.Status401Unauthorized => Unauthorized(result.Error),
                StatusCodes.Status404NotFound => NotFound(result.Error),
                _ => StatusCode(result.StatusCode, result.Error ?? new ApiErrorResponseDto())
            };
        }
    }
}
