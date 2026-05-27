using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Common.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }

        // Optional machine-readable discriminator for client-side branching on
        // specific failure modes (e.g. "EmailAlreadyExists"). Stable across
        // backend message-wording / localisation changes. See ErrorCodes.
        // JsonIgnore-when-null keeps the wire shape clean for responses that
        // don't set a code; AddJsonOptions in Program.cs does NOT set a
        // global DefaultIgnoreCondition, so the per-property attribute is the
        // load-bearing piece.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorCode { get; set; }

        // Success response with data
        public static ApiResponse<T> SuccessWithData(T data, string message = "Operation completed successfully")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        // Success response without data
        public static ApiResponse<T> SuccessWithoutData(string message = "Operation completed successfully")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Message = message
            };
        }

        // Error response with errors list
        public static ApiResponse<T> Failure(List<string> errors, string message = "Operation failed")
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = errors
            };
        }

        // Error response with single error
        public static ApiResponse<T> Failure(string error, string message = "Operation failed")
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = new List<string> { error }
            };
        }

        // Error response carrying a machine-readable ErrorCode alongside the
        // human-readable message/error. Keeps Errors populated so older
        // clients that read only `errors[]` continue to work.
        public static ApiResponse<T> Failure(string error, string errorCode, string message = "Operation failed")
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = new List<string> { error },
                ErrorCode = errorCode
            };
        }
    }
}
