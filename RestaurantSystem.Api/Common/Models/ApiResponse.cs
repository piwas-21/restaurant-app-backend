using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Common.Models
{
    public class ApiResponse<T>
    {
        // The default `Message` on every failure overload. A const because these are default
        // PARAMETER values, which C# requires to be compile-time constants.
        //
        // Worth knowing before treating it as a wording detail: this literal is the reason the
        // frontend prefers `errors[]` over `message` (apiFormErrors.ts). A controller calling the
        // one-argument Failure puts the real reason in Errors[0] and leaves Message at exactly
        // this string, so a client reading the message would show the guest the wrapper instead of
        // the cause.
        public const string DefaultFailureMessage = "Operation failed";

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
        public static ApiResponse<T> Failure(List<string> errors, string message = DefaultFailureMessage)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = errors
            };
        }

        // Error response with single error
        public static ApiResponse<T> Failure(string error, string message = DefaultFailureMessage)
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
        //
        // NOTE: Deliberately named FailureWithCode (not an overload of Failure)
        // to avoid a C# overload-resolution hazard — Failure("err", "CODE")
        // would otherwise bind to the 2-arg Failure(error, message) overload
        // (since all-defaulted params win when arg counts match), silently
        // dropping the error code into the message slot.
        public static ApiResponse<T> FailureWithCode(string error, string errorCode, string message = DefaultFailureMessage)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = new List<string> { error },
                ErrorCode = errorCode
            };
        }

        // The list counterpart, for a coded refusal that has SEVERAL reasons — a validation
        // failure with one entry per broken rule. No overload hazard with the string version
        // above: the first parameter types differ, so a call can never bind to the wrong one.
        public static ApiResponse<T> FailureWithCode(List<string> errors, string errorCode, string message = DefaultFailureMessage)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = errors,
                ErrorCode = errorCode
            };
        }
    }
}
