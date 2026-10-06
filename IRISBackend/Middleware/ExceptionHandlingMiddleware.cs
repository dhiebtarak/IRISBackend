using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

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
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "Request was aborted by the client while processing {Method} {Path}.",
                    context.Request.Method,
                    context.Request.Path);
            }
            catch (Exception exception)
            {
                var sqlException = FindSqlException(exception);
                var constraintName = sqlException is null ? null : FindConstraintName(sqlException.Message);
                var failedEntities = exception is DbUpdateException updateException
                    ? string.Join(",", updateException.Entries.Select(entry => entry.Metadata.ClrType.Name).Distinct())
                    : null;
                _logger.LogError(
                    exception,
                    "Unhandled request failure {TraceId} at {Method} {Path}. Exception type: {ExceptionType}. Inner exception: {InnerException}. SQL error: {SqlErrorNumber}. Constraint/index: {ConstraintName}. Failed entities: {FailedEntities}",
                    context.TraceIdentifier,
                    context.Request.Method,
                    context.Request.Path,
                    exception.GetType().FullName,
                    exception.InnerException?.ToString(),
                    sqlException?.Number,
                    constraintName,
                    failedEntities);

                if (context.Response.HasStarted)
                {
                    throw;
                }

                if (context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/problem+json";
                var problem = new ProblemDetails
                {
                    Type = "about:blank",
                    Title = "An unexpected server error occurred.",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = _environment.IsDevelopment()
                        ? $"{exception.GetType().Name}{(sqlException is null ? string.Empty : $" (SQL error {sqlException.Number})")}. See backend logs using the traceId for details."
                        : null,
                    Instance = context.Request.Path
                };
                problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
                await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
            }
        }

        private static SqlException? FindSqlException(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException!)
                if (current is SqlException sqlException) return sqlException;
            return null;
        }

        private static string? FindConstraintName(string message)
        {
            var match = Regex.Match(message, "(?:constraint|index) ['\\\"](?<name>[^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["name"].Value : null;
        }
    }
}
