using RestaurantSystem.Api.Features.Auth.Interfaces;
using RestaurantSystem.Api.Features.Auth.Services;
using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.ForgotPasswordCommand;

public record ForgotPasswordCommand(string Email) : ICommand<ApiResponse<string>>
{
    // Set only by the strict native command; legacy JSON cannot choose a different delivery rail.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NativeClient { get; init; }
}

public class ForgotPasswordCommandHandler : ICommandHandler<ForgotPasswordCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;
    private readonly IEmailService _emailService;
    private readonly ICustomerNativeAccountMail _nativeMail;
    private readonly IEmailLanguageResolver _languages;

    public ForgotPasswordCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<ForgotPasswordCommandHandler> logger,
        IEmailService emailService,
        ICustomerNativeAccountMail nativeMail,
        IEmailLanguageResolver languages)
    {
        _userManager = userManager;
        _logger = logger;
        _emailService = emailService;
        _nativeMail = nativeMail;
        _languages = languages;
    }

    public async Task<ApiResponse<string>> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted || command.NativeClient && user.Role != RestaurantSystem.Domain.Common.Enums.UserRole.Customer)
        {
            _logger.LogDebug("Password reset requested for an unavailable account");
            return ApiResponse<string>.SuccessWithData(
                "If the email exists in our system, a password reset link has been sent.",
                "Password reset request processed");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // The ACCOUNT's language, never this request's: this endpoint is anonymous and takes an
        // address from anyone, so rank 3 here would let a stranger choose the language a reset
        // mail arrives in on someone else's inbox.
        string? nativeFailure = null;
        try
        {
            if (command.NativeClient)
                await _nativeMail.SendAsync(_languages.ForAccount(user), user, token, CustomerNativeAccountLinks.Recover);
            else
                await _emailService.SendPasswordResetEmailAsync(_languages.ForAccount(user), user, token);
        }
        catch (Exception ex) when (command.NativeClient)
        {
            // Provider exceptions may contain the entire action URL. Keep the public result generic.
            nativeFailure = ex.GetType().Name;
        }
        if (nativeFailure is not null) _logger.LogWarning("Native reset mail failed with {ExceptionType}", nativeFailure);

        return ApiResponse<string>.SuccessWithData(
            "If the email exists in our system, a password reset link has been sent.",
            "Password reset request processed");


    }
}
