namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public interface ITableServiceSessionCurrencyPolicy
{
    Task<SessionCurrencyResult> ResolveAsync(
        Guid? serviceSessionId, string? requestedCurrency, CancellationToken cancellationToken);
}

public sealed record SessionCurrencyResult(bool Success, string? Currency, string? Error = null);
