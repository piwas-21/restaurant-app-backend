using System.Security.Claims;
using RestaurantSystem.Domain.Common.Interfaces;

namespace RestaurantSystem.Api.Common.Services
{
    /// <summary>
    /// Supplies <c>ApplicationDbContext</c> with the acting user's identifier, read straight off the
    /// ambient request's claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists instead of forwarding to <c>ICurrentUserService</c>:</b> that would be a
    /// dependency CYCLE. <c>CurrentUserService</c> takes <c>UserManager&lt;ApplicationUser&gt;</c>,
    /// which resolves <c>IUserStore</c>, which
    /// <c>AddEntityFrameworkStores&lt;ApplicationDbContext&gt;</c> binds back to the context — so
    /// constructing the context would require the context. It does not fail loudly: the test host
    /// hangs rather than throwing a circular-dependency error, which is a very expensive way to
    /// find out.
    /// </para>
    /// <para>
    /// <see cref="IHttpContextAccessor"/> is the whole dependency, and it has no path back to the
    /// context. The claim read here is the same one <c>CurrentUserService.UserId</c> reads, with the
    /// same "System" fallback; <c>AuditIdentityAgreementTests</c> pins the two together so they
    /// cannot drift.
    /// </para>
    /// <para>
    /// Deliberately does NOT parse the claim into a <see cref="Guid"/>. <c>CurrentUserService.UserId</c>
    /// uses <c>Guid.Parse</c>, which throws on a malformed claim; that is survivable inside a request
    /// handler but not inside <c>SaveChangesAsync</c>, where it would turn a bad token into a failed
    /// write. Returning the raw claim keeps every valid case identical and the invalid case harmless.
    /// </para>
    /// </remarks>
    public class HttpContextAuditIdentityProvider : IAuditIdentityProvider
    {
        /// <summary>Written when nothing is authenticated — background services, seeders, tooling.</summary>
        public const string SystemIdentifier = "System";

        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextAuditIdentityProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetAuditIdentifier()
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(userId) ? SystemIdentifier : userId;
        }
    }
}
