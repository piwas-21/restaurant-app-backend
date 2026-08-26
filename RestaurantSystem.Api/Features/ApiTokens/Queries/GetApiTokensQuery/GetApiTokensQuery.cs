using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.ApiTokens.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.ApiTokens.Queries.GetApiTokensQuery;

/// <summary>
/// List every machine token, live or not (docs/plans/API-TOKENS-PLAN.md §8). Revoked and expired
/// rows are included on purpose: they are the audit trail an admin looks at after an incident.
/// </summary>
public record GetApiTokensQuery : IQuery<ApiResponse<List<ApiTokenDto>>>;

public class GetApiTokensQueryHandler
    : IQueryHandler<GetApiTokensQuery, ApiResponse<List<ApiTokenDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetApiTokensQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<ApiTokenDto>>> Handle(
        GetApiTokensQuery query, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Never projects TokenHash. The hash is not a secret an attacker can use directly, but a
        // list endpoint that hands it out turns one XSS into an offline verification oracle.
        var tokens = await _context.ApiTokens
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var data = tokens.Select(t => new ApiTokenDto
        {
            Id = t.Id,
            Name = t.Name,
            Prefix = t.Prefix,
            Scopes = t.Scopes,
            ExpiresAt = t.ExpiresAt,
            RevokedAt = t.RevokedAt,
            LastUsedAt = t.LastUsedAt,
            CreatedAt = t.CreatedAt,
            Status = ApiTokenStatuses.Of(t, now)
        }).ToList();

        return ApiResponse<List<ApiTokenDto>>.SuccessWithData(data);
    }
}
