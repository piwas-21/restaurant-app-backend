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
    private readonly IAppleIdentityTokenVerifier _tokenVerifier;
    private readonly LoginEventHandler _loginEventHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AppleLoginCommandHandler> _logger;

    public AppleLoginCommandHandler(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        IAppleIdentityTokenVerifier tokenVerifier,
        LoginEventHandler loginEventHandler,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AppleLoginCommandHandler> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _tokenVerifier = tokenVerifier;
        _loginEventHandler = loginEventHandler;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
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

        var email = validation.Identity!.Email;
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
            var created = await CreateUserAsync(email, request);
            if (created.Error is not null)
            {
                return created.Error;
            }

            user = created.User!;
        }
        else
        {
            await RefreshNameAsync(user, request);
        }

        return await IssueTokensAsync(user);
    }

    private async Task<(ApplicationUser? User, ApiResponse<AuthResponse>? Error)> CreateUserAsync(
        string email, AppleLoginCommand request)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = Clean(request.FirstName) ?? PlaceholderFirstName,
            LastName = Clean(request.LastName) ?? PlaceholderLastName,
            EmailConfirmed = true,
            Role = UserRole.Customer,
            CreatedBy = "AppleAuth",
            RefreshToken = string.Empty, // Will be set later
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            return (null, ApiResponse<AuthResponse>.Failure(
                string.Join(", ", result.Errors.Select(e => e.Description)), "Registration failed"));
        }

        return (user, null);
    }

    /// <summary>
    /// BACKEND-NOTES §4.2. Apple hands over <c>fullName</c> only on an Apple ID's FIRST
    /// authorisation, so an account created without one stayed "Apple User" forever. An
    /// incoming non-empty name now wins over what is stored.
    /// </summary>
    private async Task RefreshNameAsync(ApplicationUser user, AppleLoginCommand request)
    {
        var firstName = Clean(request.FirstName);
        var lastName = Clean(request.LastName);

        var changed = false;

        if (firstName is not null && !string.Equals(user.FirstName, firstName, StringComparison.Ordinal))
        {
            user.FirstName = firstName;
            changed = true;
        }

        if (lastName is not null && !string.Equals(user.LastName, lastName, StringComparison.Ordinal))
        {
            user.LastName = lastName;
            changed = true;
        }

        if (changed)
        {
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                // Never fail a sign-in over a cosmetic field.
                _logger.LogWarning("Could not refresh the Apple name for {UserId}: {Errors}",
                    user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private async Task<ApiResponse<AuthResponse>> IssueTokensAsync(ApplicationUser user)
    {
        var token = _tokenService.GenerateAccessToken(user);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();

        user.RefreshToken = _tokenService.HashRefreshToken(rawRefreshToken);
        user.RefreshTokenExpiryTime = _tokenService.GetRefreshTokenExpiration();
        await _userManager.UpdateAsync(user);

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

    /// <summary>Trimmed name, or null when the client sent nothing usable.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
