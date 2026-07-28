using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOrderForQuickActionQuery;

/// <summary>The only fields the anonymous email-link actions need to decide what to render.</summary>
/// <remarks>
/// Deliberately not <c>OrderDto</c>. This is the one order lookup reachable with no credentials,
/// and <c>OrderDto</c> carries customer name, email, phone, delivery address and payment rows —
/// the same PII that #256 and #258 spent two PRs getting off the anonymous surfaces. Projecting to
/// three fields means a future addition to <c>OrderDto</c> cannot silently widen this hole.
/// </remarks>
public sealed record QuickActionOrder(Guid Id, string OrderNumber, OrderStatus Status);

/// <summary>
/// Resolves the order behind a quick-confirm / quick-cancel email link, and returns it only when
/// <paramref name="Token" /> matches the secret minted for that order.
/// </summary>
/// <remarks>
/// This replaces a <c>GetOrdersQuery</c> dispatch. That was wrong twice over: it ran the full
/// staff-scoped order search in-process on behalf of an anonymous caller — bypassing the
/// <c>[Authorize]</c> that guards the same query over HTTP — and it matched the order number by
/// SUBSTRING, taking the first row, so a prefix could resolve to someone else's order
/// (ORDER-TYPE-AVAILABILITY-PLAN §9.20).
/// </remarks>
/// <param name="OrderNumber">Matched exactly, not by substring.</param>
/// <param name="Token">The link's bearer secret. Null/empty never matches.</param>
public record GetOrderForQuickActionQuery(string OrderNumber, string? Token)
    : IQuery<QuickActionOrder?>;

public class GetOrderForQuickActionQueryHandler
    : IQueryHandler<GetOrderForQuickActionQuery, QuickActionOrder?>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetOrderForQuickActionQueryHandler> _logger;

    public GetOrderForQuickActionQueryHandler(
        ApplicationDbContext context,
        ILogger<GetOrderForQuickActionQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<QuickActionOrder?> Handle(
        GetOrderForQuickActionQuery query,
        CancellationToken cancellationToken)
    {
        // Short-circuit, NOT the security check — QuickActionTokens.Matches already rejects an
        // empty token, and deleting these four lines keeps every test in
        // OrderEmailLinkAuthorizationTests green. It exists so an unauthenticated caller spraying
        // tokenless URLs cannot make the database do a lookup per request. Keep the real guard in
        // Matches; do not add a second one here that could drift out of agreement with it.
        if (string.IsNullOrEmpty(query.Token))
        {
            return null;
        }

        var match = await _context.Orders
            .AsNoTracking()
            .Where(o => o.OrderNumber == query.OrderNumber)
            .Select(o => new { o.Id, o.OrderNumber, o.Status, o.QuickActionToken })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null || !QuickActionTokens.Matches(match.QuickActionToken, query.Token))
        {
            // Logged without the supplied token: it is a credential guess, and writing guesses to
            // the log turns a failed attempt into a stored secret if one ever succeeds elsewhere.
            _logger.LogWarning(
                "Rejected quick-action link for order {OrderNumber}: no such order, or token mismatch",
                query.OrderNumber);
            return null;
        }

        return new QuickActionOrder(match.Id, match.OrderNumber, match.Status);
    }
}
