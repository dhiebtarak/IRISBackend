using IRISBackend.DTOs;
using System.Net;
using System.Text.Json;

namespace IRISBackend.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unhandled exception while processing {Method} {Path}. Exception: {ExceptionMessage}. Inner exception: {InnerExceptionMessage}",
                    context.Request.Method,
                    context.Request.Path,
                    exception.Message,
                    exception.InnerException?.Message);

                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var response = new ApiErrorResponseDto
                {
                    Message = "An unexpected server error occurred."
                };

                if (_environment.IsDevelopment())
                {
                    response.Errors = new[]
                    {
                        exception.Message,
                        exception.InnerException?.Message ?? string.Empty,
                        exception.StackTrace ?? string.Empty
                    }
                    .Where(detail => !string.IsNullOrWhiteSpace(detail));
                }

                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
        }
    }
}
