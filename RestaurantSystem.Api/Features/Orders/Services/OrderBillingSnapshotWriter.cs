using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderBillingSnapshotWriter : IOrderBillingSnapshotWriter
{
    private readonly ApplicationDbContext _context;
    private readonly OrderBillingSnapshotOptions _options;

    public OrderBillingSnapshotWriter(
        ApplicationDbContext context,
        IOptions<OrderBillingSnapshotOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    public async Task WriteAsync(
        Order order,
        string? acceptedCurrency,
        OrderBillingEarningEvaluation? earning,
        OrderBillingRedemptionEvidence? redemption,
        CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new ConflictException("A native billing snapshot requires its order acceptance transaction.");

        var result = OrderBillingSnapshotFactory.Build(
            order, acceptedCurrency, earning, redemption, _options.MaximumUnitRows);
        _context.OrderBillingSnapshots.Add(result.Header);
        _context.OrderBillingSnapshotUnits.AddRange(result.Units);
        _context.OrderBillingSnapshotOwnerLinks.AddRange(result.OwnerLinks);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
