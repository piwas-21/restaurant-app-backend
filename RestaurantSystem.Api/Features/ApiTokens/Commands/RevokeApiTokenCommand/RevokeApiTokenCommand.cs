using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.ApiTokens.Commands.RevokeApiTokenCommand;

/// <summary>
/// Kill a machine token immediately (docs/plans/API-TOKENS-PLAN.md §6). The next request the
/// holder makes is a 401, because the authentication handler reads this row every time.
/// </summary>
public record RevokeApiTokenCommand(Guid Id) : ICommand<ApiResponse<bool>>;

public class RevokeApiTokenCommandHandler
    : ICommandHandler<RevokeApiTokenCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RevokeApiTokenCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<bool>> Handle(
        RevokeApiTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var token = await _context.ApiTokens
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new NotFoundException($"API token {command.Id} was not found.");

        // Idempotent: an admin who clicks revoke twice, or replays a request after a flaky
        // connection, must not be told the emergency action failed.
        if (token.RevokedAt is null)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.UpdatedAt = token.RevokedAt;
            token.UpdatedBy = _currentUserService.GetAuditIdentifier();
            await _context.SaveChangesAsync(cancellationToken);
        }

        // The row is KEPT, never deleted: LastUsedAt and the audit columns are how you answer
        // "what did the leaked token touch, and until when" — a question asked after revoking.
        return ApiResponse<bool>.SuccessWithData(true, "API token revoked");
    }
}
