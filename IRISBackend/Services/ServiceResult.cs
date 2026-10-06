using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public class ServiceResult<T>
    {
        private ServiceResult(T? value, ApiErrorResponseDto? error, int statusCode)
        {
            Value = value;
            Error = error;
            StatusCode = statusCode;
        }

        public T? Value { get; }

        public ApiErrorResponseDto? Error { get; }

        public int StatusCode { get; }

        public bool Succeeded => Error is null;

        public static ServiceResult<T> Success(T value, int statusCode = StatusCodes.Status200OK)
        {
            return new ServiceResult<T>(value, null, statusCode);
        }

        public static ServiceResult<T> Failure(string message, IEnumerable<string>? errors = null, int statusCode = StatusCodes.Status400BadRequest, string? code = null)
        {
            return new ServiceResult<T>(
                default,
                new ApiErrorResponseDto
                {
                    Code = code,
                    Message = message,
                    Errors = errors ?? []
                },
                statusCode);
        }
    }
}
