using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Applies one currency rule to old and new bill writers.</summary>
public sealed class TableServiceSessionCurrencyPolicy : ITableServiceSessionCurrencyPolicy
{
    private readonly ApplicationDbContext _context;

    public TableServiceSessionCurrencyPolicy(ApplicationDbContext context) => _context = context;

    public async Task<SessionCurrencyResult> ResolveAsync(
        Guid? serviceSessionId, string? requestedCurrency, CancellationToken cancellationToken)
    {
        var requested = CurrencyCode.Normalize(requestedCurrency);
        if (!serviceSessionId.HasValue)
        {
            return new SessionCurrencyResult(true, requested);
        }

        var session = await _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId.Value, cancellationToken);
        if (session is null)
        {
            return new SessionCurrencyResult(false, null, "Table service session was not found.");
        }

        var expected = CurrencyCode.Normalize(session.Currency);
        if (expected is not null && requested is not null
            && !string.Equals(expected, requested, StringComparison.OrdinalIgnoreCase))
        {
            return new SessionCurrencyResult(false, null,
                $"Payment currency must match the session currency ({expected}).");
        }

        return new SessionCurrencyResult(true, requested ?? expected);
    }
}
