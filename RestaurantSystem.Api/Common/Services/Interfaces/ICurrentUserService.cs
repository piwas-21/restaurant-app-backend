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

        /// <summary>
        /// True for back-of-house roles that legitimately read/act on *any* customer's data
        /// (cashier till, kitchen display, server floor view), as opposed to a
        /// <see cref="UserRole.Customer"/> who may only reach their own records.
        /// </summary>
        /// <remarks>
        /// Deliberately shared rather than re-derived per handler: this predicate is the
        /// dividing line for order-ownership checks, and two copies of it drifting apart is
        /// exactly how <c>GetOrderByIdQuery</c> ended up without the check that
        /// <c>GetOrdersQuery</c> had.
        /// </remarks>
        bool IsStaff => IsAdmin
            || Role == UserRole.Cashier
            || Role == UserRole.KitchenStaff
            || Role == UserRole.Server;
    }
}
