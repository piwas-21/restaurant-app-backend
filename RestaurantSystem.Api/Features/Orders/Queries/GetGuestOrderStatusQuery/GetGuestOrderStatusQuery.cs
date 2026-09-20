using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetGuestOrderStatusQuery;

/// <summary>
/// The order state a guest's confirmation screen polls while it waits for the restaurant's
/// review/approval (order confirmation flows). Returns null for "no such order OR token does not
/// match" — the caller cannot tell the two apart, exactly like the quick-action links.
/// </summary>
/// <remarks>
/// Same shape and same guard as <see cref="GetOrderForQuickActionQuery"/>: the anonymous lookup is
/// authorised by the order's 128-bit id PLUS its <c>QuickActionToken</c> bearer secret, both of
/// which the guest received on the confirmation URL at creation. A wrong token renders the same
/// empty result as an unknown order, so neither route can be used to test whether a given order
/// exists.
/// </remarks>
/// <param name="OrderId">The order's aggregate id, from the confirmation URL.</param>
/// <param name="Token">The order's <c>QuickActionToken</c>, from the confirmation URL. Null/empty never matches.</param>
public record GetGuestOrderStatusQuery(Guid OrderId, string? Token) : IQuery<GuestOrderStatusDto?>;

public class GetGuestOrderStatusQueryHandler : IQueryHandler<GetGuestOrderStatusQuery, GuestOrderStatusDto?>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetGuestOrderStatusQueryHandler> _logger;

    public GetGuestOrderStatusQueryHandler(
        ApplicationDbContext context,
        ILogger<GetGuestOrderStatusQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<GuestOrderStatusDto?> Handle(
        GetGuestOrderStatusQuery query,
        CancellationToken cancellationToken)
    {
        // Short-circuit, NOT the security check — QuickActionTokens.Matches already rejects an
        // empty token. It exists so an anonymous caller spraying tokenless URLs cannot make the
        // database do a lookup per request (same reasoning as the quick-action lookup).
        if (string.IsNullOrEmpty(query.Token))
        {
            return null;
        }

        var match = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == query.OrderId)
            .Select(o => new { o.Id, o.OrderNumber, o.Type, o.Status, o.EstimatedDeliveryTime, o.GuestStatusToken })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null || !QuickActionTokens.Matches(match.GuestStatusToken, query.Token))
        {
            // Logged without the supplied token: it is a credential guess (see the quick-action
            // lookup for why guesses are never written to the log).
            _logger.LogWarning(
                "Rejected guest status poll for order {OrderId}: no such order, or token mismatch",
                query.OrderId);
            return null;
        }

        return new GuestOrderStatusDto(match.OrderNumber, match.Type, match.Status, match.EstimatedDeliveryTime);
    }
}
