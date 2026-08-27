using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.ApiTokens.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.ApiTokens.Commands.CreateApiTokenCommand;

/// <summary>
/// Mint a scoped machine token (docs/plans/API-TOKENS-PLAN.md). Admin-only, and unreachable by a
/// token itself — see <c>ApiTokenScopeFilter</c>.
/// </summary>
public record CreateApiTokenCommand(string Name, List<string> Scopes, int ExpiresInDays)
    : ICommand<ApiResponse<CreatedApiTokenDto>>;

public class CreateApiTokenCommandHandler
    : ICommandHandler<CreateApiTokenCommand, ApiResponse<CreatedApiTokenDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateApiTokenCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<CreatedApiTokenDto>> Handle(
        CreateApiTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = DateTime.UtcNow;
        var name = command.Name.Trim();

        // Unique among LIVE tokens only. A revoked "menu seeder" must not block the replacement
        // an admin creates in the same minute, which is the normal reaction to a leak.
        var nameTaken = await _context.ApiTokens
            .AnyAsync(t => t.Name == name && t.RevokedAt == null && t.ExpiresAt > now,
                cancellationToken);

        if (nameTaken)
        {
            throw new BadRequestException($"An active API token named '{name}' already exists.");
        }

        var plaintext = ApiTokenHasher.GenerateToken();

        var token = new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = name,
            TokenHash = ApiTokenHasher.ComputeHash(plaintext),
            Prefix = ApiTokenHasher.ExtractPrefix(plaintext),
            Scopes = command.Scopes.Distinct(StringComparer.Ordinal).ToList(),
            ExpiresAt = now.AddDays(command.ExpiresInDays),
            CreatedAt = now,
            CreatedBy = _currentUserService.GetAuditIdentifier()
        };

        _context.ApiTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);

        // The plaintext leaves the process exactly here and is never stored, logged or shown
        // again. Everything else in the system sees only the hash and the prefix.
        return ApiResponse<CreatedApiTokenDto>.SuccessWithData(
            new CreatedApiTokenDto
            {
                Id = token.Id,
                Name = token.Name,
                Token = plaintext,
                Prefix = token.Prefix,
                Scopes = token.Scopes,
                ExpiresAt = token.ExpiresAt,
                CreatedAt = token.CreatedAt
            },
            "API token created");
    }
}
