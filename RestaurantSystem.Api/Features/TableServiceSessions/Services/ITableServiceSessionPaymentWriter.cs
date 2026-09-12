using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public interface ITableServiceSessionPaymentWriter
{
    Task<SessionPaymentWriteResult> ApplyAsync(
        TableServiceSession session,
        AddTableServiceSessionPaymentCommand command,
        CancellationToken cancellationToken);
}

public sealed record SessionPaymentWriteResult(
    bool Success,
    decimal Applied,
    int OrderCount,
    string? Error = null,
    bool CurrencyMismatch = false);
