namespace RestaurantSystem.Api.Common.Models;

/// <summary>
/// Stable machine-readable error codes carried on <see cref="ApiResponse{T}.ErrorCode"/>.
///
/// Codes are intentionally English PascalCase strings (not enums) so they remain
/// constant across backend localisation changes and survive JSON round-trips
/// without depending on enum serialization config.
///
/// Adding a code is a public API contract change — coordinate with the
/// frontend (and any other API consumer) before introducing one.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Returned by the customer registration endpoint when an account with the
    /// submitted email already exists. Frontend uses this to surface an inline
    /// "Email already registered" hint without substring-matching the message.
    /// </summary>
    public const string EmailAlreadyExists = "EmailAlreadyExists";
}
