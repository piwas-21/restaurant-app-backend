using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.SendEmailVerificationCommand;

public record SendEmailVerificationCommand(string Email) : ICommand<ApiResponse<string>>;

public class SendEmailVerificationCommandHandler : ICommandHandler<SendEmailVerificationCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<SendEmailVerificationCommandHandler> _logger;

    public SendEmailVerificationCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        ILogger<SendEmailVerificationCommandHandler> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(SendEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted)
        {
            _logger.LogWarning("Email verification requested for non-existent email: {Email}", command.Email);
            return ApiResponse<string>.SuccessWithData(
                "If the email exists in our system, a verification link has been sent.",
                "Email verification request processed");
        }

        if (user.EmailConfirmed)
        {
            return ApiResponse<string>.SuccessWithData(
                "Email is already verified.",
                "Email verification not needed");
        }

        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailService.SendEmailVerificationAsync(user, token);

            _logger.LogInformation("Email verification sent successfully for user {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email verification for user {UserId}", user.Id);
            // Don't reveal the error to the user for security reasons
        }

        return ApiResponse<string>.SuccessWithData(
            "If the email exists in our system, a verification link has been sent.",
            "Email verification request processed");
    }
}
