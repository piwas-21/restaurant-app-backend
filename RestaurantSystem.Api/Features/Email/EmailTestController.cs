using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Email;

[ApiController]
[Route("api/[controller]")]
public class EmailTestController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<EmailTestController> _logger;

    public EmailTestController(
        IEmailService emailService,
        IWebHostEnvironment environment,
        ILogger<EmailTestController> logger)
    {
        _emailService = emailService;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Test basic email sending functionality
    /// </summary>
    [HttpPost("send-test-email")]
    public async Task<ActionResult<ApiResponse<string>>> SendTestEmail([FromBody] TestEmailRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return BadRequest(ApiResponse<string>.Failure("Email testing is only available in development environment"));
        }

        try
        {
            await _emailService.SendEmailAsync(
                request.ToEmail,
                "Test Email from Restaurant System",
                "<h1>Test Email</h1><p>This is a test email from Restaurant System API.</p>",
                "Test Email - This is a test email from Restaurant System API.");

            return Ok(ApiResponse<string>.SuccessWithData("Test email sent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test email to {Email}", request.ToEmail);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send test email: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test password reset email template
    /// </summary>
    [HttpPost("test-password-reset-email")]
    public async Task<ActionResult<ApiResponse<string>>> TestPasswordResetEmail([FromBody] TestEmailRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return BadRequest(ApiResponse<string>.Failure("Email testing is only available in development environment"));
        }

        try
        {
            var testUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = "Test",
                LastName = "User",
                Email = request.ToEmail,
                UserName = request.ToEmail,
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                RefreshToken = "test-refresh-token"
            };

            await _emailService.SendPasswordResetEmailAsync(testUser, "test-reset-token");

            return Ok(ApiResponse<string>.SuccessWithData("Password reset email test sent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test password reset email to {Email}", request.ToEmail);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send test password reset email: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test welcome email template
    /// </summary>
    [HttpPost("test-welcome-email")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> TestWelcomeEmail([FromBody] TestWelcomeEmailRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return BadRequest(ApiResponse<string>.Failure("Email testing is only available in development environment"));
        }

        try
        {
            var testUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.ToEmail,
                UserName = request.ToEmail,
                Role = request.Role,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                RefreshToken = "test-refresh-token"
            };

            await _emailService.SendWelcomeEmailAsync(testUser);

            return Ok(ApiResponse<string>.SuccessWithData("Welcome email test sent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test welcome email to {Email}", request.ToEmail);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send test welcome email: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test password changed notification email
    /// </summary>
    [HttpPost("test-password-changed-email")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> TestPasswordChangedEmail([FromBody] TestEmailRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return BadRequest(ApiResponse<string>.Failure("Email testing is only available in development environment"));
        }

        try
        {
            var testUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = "Test",
                LastName = "User",
                Email = request.ToEmail,
                UserName = request.ToEmail,
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                RefreshToken = "test-refresh-token"
            };

            await _emailService.SendPasswordChangedNotificationAsync(testUser);

            return Ok(ApiResponse<string>.SuccessWithData("Password changed notification test sent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test password changed email to {Email}", request.ToEmail);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send test password changed email: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test email verification email template
    /// </summary>
    [HttpPost("test-email-verification")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> TestEmailVerification([FromBody] TestEmailRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return BadRequest(ApiResponse<string>.Failure("Email testing is only available in development environment"));
        }

        try
        {
            var testUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = "Test",
                LastName = "User",
                Email = request.ToEmail,
                UserName = request.ToEmail,
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System",
                RefreshToken = "test-refresh-token"
            };

            await _emailService.SendEmailVerificationAsync(testUser, "test-verification-token");

            return Ok(ApiResponse<string>.SuccessWithData("Email verification test sent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test email verification to {Email}", request.ToEmail);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send test email verification: {ex.Message}"));
        }
    }

    /// <summary>
    /// Test bulk email sending
    /// </summary>
    [HttpPost("test-bulk-email")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> TestBulkEmail([FromBody] TestBulkEmailRequest request)
    {
        if (!_environment.IsDevelopment())
        {
            return BadRequest(ApiResponse<string>.Failure("Email testing is only available in development environment"));
        }

        try
        {
            await _emailService.SendBulkEmailAsync(
                request.Recipients,
                "Bulk Test Email from Restaurant System",
                "<h1>Bulk Test Email</h1><p>This is a bulk test email from Restaurant System API.</p>",
                "Bulk Test Email - This is a bulk test email from Restaurant System API.");

            return Ok(ApiResponse<string>.SuccessWithData($"Bulk email sent successfully to {request.Recipients.Count} recipients"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bulk test email to {Count} recipients", request.Recipients.Count);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send bulk test email: {ex.Message}"));
        }
    }
}

public record TestEmailRequest
{
    public string ToEmail { get; init; } = null!;
}

public record TestWelcomeEmailRequest
{
    public string ToEmail { get; init; } = null!;
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public UserRole Role { get; init; } = UserRole.Customer;
}

public record TestBulkEmailRequest
{
    public List<string> Recipients { get; init; } = new();
}
