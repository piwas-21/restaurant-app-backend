using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

/// <inheritdoc />
public sealed class RetainedCustomerDataScrubber : IRetainedCustomerDataScrubber
{
    private const string Erased = "[erased]";
    private readonly ApplicationDbContext _context;

    public RetainedCustomerDataScrubber(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task ScrubAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Scrub address snapshots before clearing Order.UserId. The correlated subquery stays
        // server-side, so even a long-lived customer cannot make erasure load every order id.
        await _context.OrderAddresses
            .Where(address => _context.Orders
                .IgnoreQueryFilters()
                .Any(order => order.Id == address.OrderId && order.UserId == userId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(address => address.Label, Erased)
                .SetProperty(address => address.AddressLine1, Erased)
                .SetProperty(address => address.AddressLine2, (string?)null)
                .SetProperty(address => address.City, Erased)
                .SetProperty(address => address.State, (string?)null)
                .SetProperty(address => address.PostalCode, Erased)
                .SetProperty(address => address.Country, Erased)
                .SetProperty(address => address.Phone, (string?)null)
                .SetProperty(address => address.Latitude, (double?)null)
                .SetProperty(address => address.Longitude, (double?)null)
                .SetProperty(address => address.DeliveryInstructions, (string?)null), cancellationToken);

        // Unlink discount rules first. Orders owned by this user are bumped by the scrub below;
        // retained orders owned by someone else receive their one version bump here.
        // soft-delete-bypass: hidden orders can retain the rule FK and block erasure.
        await _context.Orders
            .IgnoreQueryFilters()
            .Where(order => order.CustomerDiscountRule != null
                && order.CustomerDiscountRule.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.CustomerDiscountRuleId, (Guid?)null)
                .SetProperty(order => order.Version,
                    order => order.UserId == userId ? order.Version : order.Version + 1),
                cancellationToken);

        // soft-delete-bypass: retained orders keep their financial row but lose customer PII.
        await _context.Orders
            .IgnoreQueryFilters()
            .Where(order => order.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.UserId, (Guid?)null)
                .SetProperty(order => order.Version, order => order.Version + 1)
                .SetProperty(order => order.CustomerName, (string?)null)
                .SetProperty(order => order.CustomerEmail, (string?)null)
                .SetProperty(order => order.CustomerPhone, (string?)null)
                .SetProperty(order => order.Notes, (string?)null)
                .SetProperty(order => order.CancellationReason, (string?)null)
                .SetProperty(order => order.Focus!.Reason, (string?)null), cancellationToken);

        // Reservation contact columns are non-nullable; retain a tombstone there and clear free text.
        await _context.Reservations
            .Where(reservation => reservation.CustomerId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(reservation => reservation.CustomerId, (Guid?)null)
                .SetProperty(reservation => reservation.CustomerName, Erased)
                .SetProperty(reservation => reservation.CustomerEmail, Erased)
                .SetProperty(reservation => reservation.CustomerPhone, Erased)
                .SetProperty(reservation => reservation.SpecialRequests, (string?)null)
                .SetProperty(reservation => reservation.Notes, (string?)null), cancellationToken);

        // soft-delete-bypass: permanent erasure includes rules previously marked deleted.
        await _context.CustomerDiscountRules
            .IgnoreQueryFilters()
            .Where(rule => rule.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
