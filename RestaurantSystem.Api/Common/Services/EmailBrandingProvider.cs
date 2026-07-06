using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Reads the <c>RestaurantInfo</c> singleton row and maps it to <see cref="EmailBranding"/>
/// (issue #115). The row is migration-seeded, so a missing row is unexpected; the fallback
/// below is belt-and-braces rather than an expected code path.
/// </summary>
public class EmailBrandingProvider : IEmailBrandingProvider
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<EmailBrandingProvider> _logger;
    private EmailBranding? _cached;

    public EmailBrandingProvider(ApplicationDbContext context, ILogger<EmailBrandingProvider> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<EmailBranding> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var info = await _context.RestaurantInfo.AsNoTracking().FirstOrDefaultAsync(ct);

        if (info is null)
        {
            _logger.LogWarning("RestaurantInfo row missing; falling back to default email branding");
            _cached = new EmailBranding("Restaurant", string.Empty, string.Empty);
            return _cached;
        }

        _cached = new EmailBranding(info.Name, info.City, info.Email);
        return _cached;
    }
}
