using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.User.Commands.RequestAccountDeletionCommand;

public record RequestAccountDeletionCommand(Guid UserId) : ICommand<ApiResponse<string>>;

public class RequestAccountDeletionCommandHandler : ICommandHandler<RequestAccountDeletionCommand, ApiResponse<string>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<RequestAccountDeletionCommandHandler> _logger;

    public RequestAccountDeletionCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<RequestAccountDeletionCommandHandler> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(RequestAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            return ApiResponse<string>.Failure("User not found");
        }

        // Schedule deletion for 30 days from now
        user.DeletionScheduledAt = DateTime.UtcNow.AddDays(30);
        await _userManager.UpdateAsync(user);

        // Generate deletion token
        var token = await _userManager.GenerateUserTokenAsync(user, "Default", "AccountDeletion");

        // Construct URLs
        var deleteUrl = $"{_emailSettings.FrontendBaseUrl}/delete-account?token={Uri.EscapeDataString(token)}&userId={user.Id}";
        var cancelUrl = $"{_emailSettings.FrontendBaseUrl}/auth/login"; // Login cancels usage

        // Send email (non-fatal — deletion is already scheduled in the DB)
        try
        {
            await _emailService.SendAccountDeletionEmailAsync(
                user.Email!,
                user.FirstName,
                user.LastName,
                deleteUrl,
                cancelUrl,
                user.DeletionScheduledAt.Value);
        }
        catch (Exception emailEx)
        {
            _logger.LogError(emailEx, "Failed to send account deletion email for user {UserId}. Deletion is still scheduled.", user.Id);
        }

        _logger.LogInformation("Account deletion requested for user {UserId}. Scheduled for {DeletionDate}", user.Id, user.DeletionScheduledAt);

        return ApiResponse<string>.SuccessWithData("Deletion request received. Please check your email to confirm specific actions.");
    }
}
