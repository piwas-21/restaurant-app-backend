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
        /// True for any authenticated non-Customer account — Admin, Cashier, KitchenStaff or Server.
        /// The single definition of "staff"; use it rather than re-listing roles.
        /// </summary>
        /// <remarks>
        /// There were three hand-rolled copies of this before §9.19: <c>GetOrdersQuery</c> listed the
        /// four roles, <c>OrderChannelGuard</c> phrased it as "authenticated and not a Customer", and
        /// the new ownership check would have been a fourth. They agree TODAY only because the enum
        /// happens to have exactly five members — add a sixth role and the two phrasings diverge
        /// silently, one of them deciding who may read another customer's order and the other who may
        /// override a channel restriction. Neither is a place to discover a drift.
        /// <para>
        /// Deliberately role-based, not permission-based: this codebase has no permission model, and
        /// inventing one here would be a bigger change than the check it serves.
        /// </para>
        /// </remarks>
        bool IsStaff => IsAuthenticated && Role is not null && Role != UserRole.Customer;
    }
}
