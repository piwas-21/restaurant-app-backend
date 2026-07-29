using Microsoft.AspNetCore.Http;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using System.Security.Claims;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// Pins <see cref="HttpContextAuditIdentityProvider"/> and
/// <see cref="ICurrentUserService.GetAuditIdentifier"/> to the same answer.
/// </summary>
/// <remarks>
/// They are two implementations of one rule, and they exist separately only because forwarding the
/// context's provider to <c>ICurrentUserService</c> is a dependency cycle (see the provider's
/// remarks). Two copies of a rule drifting apart is a failure this codebase has already had — it is
/// how <c>GetOrderByIdQuery</c> ended up without the ownership check <c>GetOrdersQuery</c> had. This
/// asserts the agreement instead of trusting a comment.
///
/// No database is needed: both read an <see cref="IHttpContextAccessor"/>, so a fabricated context
/// exercises the real code paths.
/// </remarks>
public class AuditIdentityAgreementTests
{
    private sealed class ClaimsOnlyCurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _accessor;

        public ClaimsOnlyCurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

        // Mirrors CurrentUserService.UserId exactly — the property GetAuditIdentifier's default
        // implementation is built on. UserManager is the only reason the real class cannot be used
        // here, and it plays no part in the audit identifier.
        public Guid? UserId
        {
            get
            {
                var userId = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
                return !string.IsNullOrEmpty(userId) ? Guid.Parse(userId) : null;
            }
        }

        public string? UserName => null;
        public string? Email => null;
        public UserRole? Role => null;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsAdmin => false;
        public Task<ApplicationUser?> GetUserAsync() => Task.FromResult<ApplicationUser?>(null);
    }

    private static HttpContextAccessor AccessorFor(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"))
        };

        return new HttpContextAccessor { HttpContext = context };
    }

    [Fact]
    public void BothAgree_ForAnAuthenticatedUser()
    {
        var userId = Guid.NewGuid();
        var accessor = AccessorFor(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        var fromProvider = new HttpContextAuditIdentityProvider(accessor).GetAuditIdentifier();
        var fromCurrentUser = ((ICurrentUserService)new ClaimsOnlyCurrentUserService(accessor)).GetAuditIdentifier();

        Assert.Equal(userId.ToString(), fromProvider);
        Assert.Equal(fromCurrentUser, fromProvider);
    }

    [Fact]
    public void BothAgree_OnSystem_WhenNoUserIsAuthenticated()
    {
        var accessor = AccessorFor();

        var fromProvider = new HttpContextAuditIdentityProvider(accessor).GetAuditIdentifier();
        var fromCurrentUser = ((ICurrentUserService)new ClaimsOnlyCurrentUserService(accessor)).GetAuditIdentifier();

        Assert.Equal(HttpContextAuditIdentityProvider.SystemIdentifier, fromProvider);
        Assert.Equal(fromCurrentUser, fromProvider);
    }

    [Fact]
    public void Provider_FallsBackToSystem_WithNoHttpContextAtAll()
    {
        // Background services and seeders save outside any request. This is the path that decides
        // what BasketCleanupService's writes are attributed to.
        var fromProvider = new HttpContextAuditIdentityProvider(new HttpContextAccessor())
            .GetAuditIdentifier();

        Assert.Equal(HttpContextAuditIdentityProvider.SystemIdentifier, fromProvider);
    }
}
