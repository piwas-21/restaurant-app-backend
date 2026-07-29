using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Common.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Services.Interfaces
{
    public interface ICurrentUserService : IAuditIdentityProvider
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
        /// <remarks>
        /// Satisfies <see cref="IAuditIdentityProvider"/> so the two can be compared in a test —
        /// NOT so this can be registered as the context's provider. Do not wire it that way:
        /// <c>ApplicationDbContext</c> resolves <c>HttpContextAuditIdentityProvider</c> instead,
        /// because forwarding to this service is a dependency cycle through
        /// <c>UserManager</c>/<c>IUserStore</c> that hangs the host rather than throwing.
        /// <c>AuditIdentityAgreementTests</c> pins the two to the same answer.
        /// </remarks>
        string IAuditIdentityProvider.GetAuditIdentifier() => UserId?.ToString() ?? "System";

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
