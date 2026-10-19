using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;
using Microsoft.AspNetCore.Identity;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.User.Commands.RequestAccountDeletionCommand;

public record RequestAccountDeletionCommand(Guid UserId, [property: JsonIgnore] bool CustomerOnly = false) : ICommand<ApiResponse<string>>;

public class RequestAccountDeletionCommandHandler : ICommandHandler<RequestAccountDeletionCommand, ApiResponse<string>>
{
    private const int GracePeriodDays = 30;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<RequestAccountDeletionCommandHandler> _logger;

    public RequestAccountDeletionCommandHandler(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IEmailLanguageResolver languages,
        IOptions<EmailSettings> emailSettings,
        ILogger<RequestAccountDeletionCommandHandler> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _languages = languages;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<string>> Handle(RequestAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null || user.IsDeleted)
        {
            return ApiResponse<string>.Failure("User not found");
        }

        if (command.CustomerOnly && user.Role != UserRole.Customer)
            throw new NotFoundException("Customer account unavailable.");

        // The persisted schedule is the recovery evidence. Repeated requests neither extend it
        // nor generate another confirmation email after an acknowledged or unknown response.
        if (user.DeletionScheduledAt.HasValue)
            return ApiResponse<string>.SuccessWithData("Account deletion is already scheduled.");
        var scheduledAt = DateTime.UtcNow.AddDays(GracePeriodDays);
        user.DeletionScheduledAt = scheduledAt;
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded)
            throw new ConflictException("Account deletion could not be confirmed. Check its current status.");

        // Generate deletion token
        var token = await _userManager.GenerateUserTokenAsync(user, "Default", "AccountDeletion");

        // Construct URLs
        var deleteUrl = $"{_emailSettings.FrontendBaseUrl}/delete-account?token={Uri.EscapeDataString(token)}&userId={user.Id}";
        var cancelUrl = $"{_emailSettings.FrontendBaseUrl}/auth/login"; // Login cancels usage

        if (!await TrySendEmailAsync(user, deleteUrl, cancelUrl, scheduledAt))
            _logger.LogError("Account deletion email could not be delivered for user {UserId}; deletion remains scheduled.", user.Id);

        _logger.LogInformation("Account deletion requested for user {UserId}. Scheduled for {DeletionDate}", user.Id, user.DeletionScheduledAt);

        return ApiResponse<string>.SuccessWithData("Deletion request received. Please check your email to confirm specific actions.");
    }

    private async Task<bool> TrySendEmailAsync(ApplicationUser user, string deleteUrl, string cancelUrl, DateTime scheduledAt)
    {
        try
        {
            await _emailService.SendAccountDeletionEmailAsync(_languages.ForAccount(user), user.Email!,
                user.FirstName, user.LastName, deleteUrl, cancelUrl, scheduledAt);
            return true;
        }
        catch (Exception)
        {
            // Transport exceptions may contain private contact/token URLs; retain only the result.
            return false;
        }
    }

}
