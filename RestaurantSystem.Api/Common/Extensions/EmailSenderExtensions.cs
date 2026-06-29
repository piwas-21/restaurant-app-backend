using System.Net.Http.Headers;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

/// <summary>
/// Registers the email transport (<see cref="IEmailSender"/>) chosen by
/// <c>EmailSettings:Provider</c>. Mirrors the file-storage provider pattern.
/// </summary>
public static class EmailSenderExtensions
{
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("EmailSettings").Get<EmailSettings>() ?? new EmailSettings();

        if (string.Equals(settings.Provider, "Resend", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
            {
                client.BaseAddress = new Uri(settings.ResendBaseUrl);
                client.Timeout = TimeSpan.FromMilliseconds(settings.TimeoutMs);
                if (!string.IsNullOrEmpty(settings.ResendApiKey))
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
            });
        }
        else
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
