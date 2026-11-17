using IRISBackend.DTOs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace IRISBackend.Services;

public static class SessionAuthenticationResponses
{
    public const string ExpiredCode = "SESSION_EXPIRED";
    public const string ExpiredMessage = "Your session has reached its limit. Re-login to continue.";
    public const string FailureKey = "IRIS.SessionFailure";

    public static Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        if (context.Exception is SecurityTokenExpiredException)
            context.HttpContext.Items[FailureKey] = ExpiredCode;
        return Task.CompletedTask;
    }

    public static async Task Challenge(JwtBearerChallengeContext context)
    {
        if (context.HttpContext.Items[FailureKey] is not string code) return;
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponseDto
        {
            Code = code,
            Message = code == ExpiredCode ? ExpiredMessage : "Your session is no longer available. Re-login to continue."
        });
    }
}
