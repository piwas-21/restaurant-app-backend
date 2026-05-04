using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services.Interfaces
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? UserName { get; }
        string? Email { get; }
        UserRole? Role { get; }
        bool IsAuthenticated { get; }
        bool IsAdmin { get; }
        Task<ApplicationUser?> GetUserAsync();

        /// <summary>
        /// Returns the current user's ID as a string for audit fields (CreatedBy/UpdatedBy),
        /// or "System" if no user is authenticated.
        /// </summary>
        string GetAuditIdentifier() => UserId?.ToString() ?? "System";
    }
}
