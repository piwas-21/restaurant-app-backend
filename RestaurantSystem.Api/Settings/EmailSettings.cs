using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Configuration settings for email service
/// </summary>
public class EmailSettings
{
    /// <summary>
    /// SMTP server host
    /// </summary>
    [Required]
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port
    /// </summary>
    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Whether to use SSL/TLS encryption
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// SMTP username for authentication
    /// </summary>
    [Required]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password for authentication
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// From email address
    /// </summary>
    [Required]
    [EmailAddress]
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// From display name
    /// </summary>
    [Required]
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Admin email address for notifications
    /// </summary>
    [Required]
    [EmailAddress]
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// Whether to use authentication
    /// </summary>
    public bool UseAuthentication { get; set; } = true;

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Base URL for the frontend application (used in email links).
    /// Required: must be configured per environment (no default — emails would otherwise
    /// silently link to localhost in production).
    /// </summary>
    [Required]
    [Url]
    public string FrontendBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the backend API (used in email action links).
    /// Required: see FrontendBaseUrl note.
    /// </summary>
    [Required]
    [Url]
    public string BackendBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether emails are enabled (useful for development/testing)
    /// </summary>
    public bool EmailsEnabled { get; set; } = true;

    /// <summary>
    /// Whether to log emails instead of sending them (useful for development)
    /// </summary>
    public bool LogEmailsOnly { get; set; } = false;

    /// <summary>
    /// Maximum number of retry attempts for failed emails
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Validates the email settings configuration
    /// </summary>
    public void Validate()
    {
        if (EmailsEnabled && !LogEmailsOnly)
        {
            if (string.IsNullOrEmpty(SmtpHost))
                throw new InvalidOperationException("SMTP Host must be configured when emails are enabled");

            if (string.IsNullOrEmpty(Username) && UseAuthentication)
                throw new InvalidOperationException("SMTP Username must be configured when authentication is enabled");

            if (string.IsNullOrEmpty(Password) && UseAuthentication)
                throw new InvalidOperationException("SMTP Password must be configured when authentication is enabled");

            if (string.IsNullOrEmpty(FromEmail))
                throw new InvalidOperationException("From Email must be configured");

            if (string.IsNullOrEmpty(FromName))
                throw new InvalidOperationException("From Name must be configured");

            if (SmtpPort <= 0 || SmtpPort > 65535)
                throw new InvalidOperationException("SMTP Port must be between 1 and 65535");
        }
    }
}
