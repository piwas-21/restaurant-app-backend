using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Queries.HasPasswordQuery;

/// <summary>
/// Answers "does the signed-in account have a local password at all?".
///
/// An account created through Google or Apple sign-in has no password hash, so
/// <c>POST /api/Auth/change-password</c> — which verifies a current password — can never succeed
/// for it. Without this query a client cannot tell that case apart from a wrong password, so it
/// shows a form the user can never submit successfully (mobile BACKEND-NOTES item 3).
///
/// Carries no payload on purpose: the account is resolved from the bearer token, never from the
/// request, so one caller can never ask about another caller's account.
/// </summary>
public record HasPasswordQuery : IQuery<ApiResponse<bool>>;

public class HasPasswordQueryHandler : IQueryHandler<HasPasswordQuery, ApiResponse<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;

    public HasPasswordQueryHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<bool>> Handle(HasPasswordQuery query, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetUserAsync();

        if (user is null)
        {
            // [Authorize] already refused a request without a token, so reaching here means the
            // token is valid but its account is gone (deleted, or soft-deleted behind the global
            // filter). Same 401 as no token at all: from the client's side the session is over.
            throw new UnauthorizedAccessException("User not authenticated");
        }

        // The Identity primitive, not a PasswordHash null-check of our own: HasPasswordAsync asks
        // the user store, which is the same source ChangePasswordAsync/AddPasswordAsync act on.
        var hasPassword = await _userManager.HasPasswordAsync(user);

        return ApiResponse<bool>.SuccessWithData(hasPassword);
    }
}
