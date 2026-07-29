namespace RestaurantSystem.Domain.Common.Interfaces
{
    /// <summary>
    /// Supplies the identifier written into <see cref="IAuditable.CreatedBy"/> /
    /// <see cref="IAuditable.UpdatedBy"/> and <see cref="ISoftDelete.DeletedBy"/> when a save
    /// reaches the database without a handler having stamped them.
    /// </summary>
    /// <remarks>
    /// This exists so <c>ApplicationDbContext</c> can ask "who is acting?" without referencing
    /// <c>ICurrentUserService</c>, which lives in the API layer: Infrastructure depends only on
    /// Domain (CLAUDE.md §3), so the dependency has to be inverted through here.
    ///
    /// There are deliberately TWO implementations of the rule — <c>ICurrentUserService</c>'s and
    /// <c>HttpContextAuditIdentityProvider</c>'s — because collapsing them into one by forwarding
    /// the provider to <c>ICurrentUserService</c> is a dependency cycle through
    /// <c>UserManager</c>/<c>IUserStore</c>, and it hangs rather than throwing.
    /// <c>AuditIdentityAgreementTests</c> asserts the two agree, so the duplication cannot drift.
    /// </remarks>
    public interface IAuditIdentityProvider
    {
        /// <summary>
        /// The current actor's identifier, or <c>"System"</c> when nothing is authenticated
        /// (background services, seeders, design-time tooling).
        /// </summary>
        string GetAuditIdentifier();
    }
}
