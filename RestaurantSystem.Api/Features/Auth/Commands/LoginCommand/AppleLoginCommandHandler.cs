using RestaurantSystem.Api.Features.Auth.Services;
using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Handlers;
using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;

/// <summary>
/// Signs a user in from a VERIFIED Apple identity token. Verification itself is
/// <see cref="IAppleIdentityTokenVerifier"/>'s job; nothing here reads a claim the validator
/// has not vouched for (BACKEND-NOTES §4.1).
/// </summary>
public class AppleLoginCommandHandler : ICommandHandler<AppleLoginCommand, ApiResponse<AuthResponse>>
{
    /// <summary>
    /// What an account gets called when Apple released no name — which is every login after an
    /// Apple ID's FIRST authorisation. Kept rather than left empty on purpose: an empty name
    /// fails <c>UpdateUserProfileCommandValidator</c>, so a nameless account could not save an
    /// unrelated profile change (phone, language) without inventing a name first.
    /// </summary>
    private const string PlaceholderFirstName = "Apple";
    private const string PlaceholderLastName = "User";

    /// <summary>One message for every rejected token — the reason goes to the log, not the wire.</summary>
    private const string InvalidTokenError = "The provided Apple token is invalid.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IRefreshSessionService _sessions;
    private readonly IAppleAccountNameUpdater _names;
    private readonly IAppleIdentityTokenVerifier _tokenVerifier;
    private readonly LoginEventHandler _loginEventHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AppleLoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IRefreshSessionService sessions,
        IAppleAccountNameUpdater names,
        IAppleIdentityTokenVerifier tokenVerifier,
        LoginEventHandler loginEventHandler,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _sessions = sessions;
        _names = names;
        _tokenVerifier = tokenVerifier;
        _loginEventHandler = loginEventHandler;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(AppleLoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _tokenVerifier.ValidateAsync(request.IdToken, cancellationToken);

        if (!validation.IsValid)
        {
            return validation.IsUnavailable
                ? ApiResponse<AuthResponse>.FailureWithCode(
                    "Apple sign-in is temporarily unavailable.",
                    ErrorCodes.AppleLoginUnavailable,
                    "Apple login unavailable")
                : ApiResponse<AuthResponse>.FailureWithCode(
                    InvalidTokenError, ErrorCodes.InvalidAppleToken, "Invalid token");
        }

        var email = validation.Identity?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            // "Hide My Email" still releases a relay address, so an absent email means the app
            // asked for no email scope. There is no `sub` column to look the account up by yet.
            return ApiResponse<AuthResponse>.Failure(
                "Could not retrieve email from Apple token.", "Email missing");
        }

        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            var (createdUser, createError) = await CreateUserAsync(email, request);
            if (createdUser is null)
            {
                return ApiResponse<AuthResponse>.Failure(createError, "Registration failed");
            }

            user = createdUser;
        }
        else
        {
            await _names.UpdateAsync(user, request.FirstName, request.LastName);
        }

        return await IssueTokensAsync(user);
    }

    /// <summary>
    /// Creates the account, or returns the reason it could not be created. The user and the error
    /// are a pair on purpose: exactly one of them is meaningful, and the caller decides by asking
    /// whether the user is null — so no call site needs a null-forgiving operator.
    /// </summary>
    private async Task<(ApplicationUser? User, string Error)> CreateUserAsync(
        string email, AppleLoginCommand request)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = AppleAccountNameRules.Clean(request.FirstName) ?? PlaceholderFirstName,
            LastName = AppleAccountNameRules.Clean(request.LastName) ?? PlaceholderLastName,
            EmailConfirmed = true,
            Role = UserRole.Customer,
            CreatedBy = "AppleAuth",
            RefreshToken = string.Empty, // Will be set later
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            return (null, string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return (user, string.Empty);
    }

    private async Task<ApiResponse<AuthResponse>> IssueTokensAsync(ApplicationUser user)
    {
        var token = _tokenService.GenerateAccessToken(user);
        var rawRefreshToken = await _sessions.IssueAsync(user, CancellationToken.None);

        // Merge anonymous basket if session ID exists
        var sessionId = _httpContextAccessor.HttpContext?.Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _loginEventHandler.HandleUserLogin(user.Id, sessionId);
        }

        return ApiResponse<AuthResponse>.SuccessWithData(new AuthResponse
        {
            AccessToken = token,
            RefreshToken = rawRefreshToken,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            UserId = user.Id,
            Expiration = _tokenService.GetAccessTokenExpiration()
        });
    }

}
