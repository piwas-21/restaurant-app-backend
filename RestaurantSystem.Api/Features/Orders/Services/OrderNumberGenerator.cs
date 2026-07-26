using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Allocates the human-facing daily order number (<c>yyyyMMdd</c> + a 4-digit sequence).
/// </summary>
/// <remarks>
/// Extracted verbatim from <c>CreateOrderCommandHandler</c>, which had 2 LOC of headroom against the
/// 200-LOC handler limit. Behaviour is unchanged.
/// </remarks>
public class OrderNumberGenerator : IOrderNumberGenerator
{
    private readonly ApplicationDbContext _context;

    public OrderNumberGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        var lastOrder = await _context.Orders
            .Where(o => o.OrderNumber.StartsWith(date))
            .OrderByDescending(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int sequence = 1;
        if (lastOrder != null)
        {
            var lastSequence = lastOrder.OrderNumber.Substring(8);
            if (int.TryParse(lastSequence, out var seq))
            {
                sequence = seq + 1;
            }
        }

        return $"{date}{sequence:D4}";
    }
}
