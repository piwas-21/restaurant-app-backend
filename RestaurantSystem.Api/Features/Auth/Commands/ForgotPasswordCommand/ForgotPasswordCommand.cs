using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.Commands.ForgotPasswordCommand;

public record ForgotPasswordCommand(string Email) : ICommand<ApiResponse<string>>;

public class ForgotPasswordCommandHandler : ICommandHandler<ForgotPasswordCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;
    private readonly IEmailService _emailService;


    public ForgotPasswordCommandHandler(UserManager<ApplicationUser> userManager, ILogger<ForgotPasswordCommandHandler> logger, IEmailService emailService)
    {
        _userManager = userManager;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<ApiResponse<string>> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(command.Email);

        if (user == null || user.IsDeleted)
        {
            _logger.LogWarning("Password reset requested for non-existent email: {Email}", command.Email);
            return ApiResponse<string>.SuccessWithData(
                "If the email exists in our system, a password reset link has been sent.",
                "Password reset request processed");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        await _emailService.SendPasswordResetEmailAsync(user, token);

        return ApiResponse<string>.SuccessWithData(
            "If the email exists in our system, a password reset link has been sent.",
            "Password reset request processed");


    }
}
