using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentRangeValidator
{
    internal static async Task ValidateAsync(
        ApplicationDbContext context,
        Guid orderId,
        OrderAmendmentQuoteRequest request,
        IReadOnlyDictionary<Guid, int> itemQuantities,
        CancellationToken cancellationToken)
    {
        var (requested, instructions) = OrderAmendmentRequestedRanges.Read(request, itemQuantities);

        if (requested.Count == 0 && instructions.Count == 0)
            return;

        var priorChanges = await context.Set<RestaurantSystem.Domain.Entities.OrderAmendment>()
            .AsNoTracking()
            .Where(amendment => amendment.SourceOrderId == orderId
                && amendment.State == OrderAmendmentState.Committed)
            .Select(amendment => amendment.ChangesJson)
            .ToListAsync(cancellationToken);

        foreach (var prior in priorChanges.SelectMany(json =>
                     OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(json)))
            ValidatePrior(prior, requested, instructions);
    }

    private static void ValidatePrior(
        OrderAmendmentChangeSnapshot prior,
        Dictionary<Guid, List<(int Start, int End)>> requested, HashSet<Guid> instructions)
    {
        if (prior.Kind == OrderAmendmentChangeKind.InstructionChange)
        {
            if (instructions.Contains(prior.OrderItemId) || requested.ContainsKey(prior.OrderItemId))
                throw new ConflictException("This source line already has a committed instruction change.");
            return;
        }
        if (instructions.Contains(prior.OrderItemId))
            throw new ConflictException("This source line has committed quantity changes and cannot be changed as a whole.");
        if (!requested.TryGetValue(prior.OrderItemId, out var ranges))
            return;
        var priorEnd = (long)prior.StartOrdinal + prior.Quantity - 1;
        if (ranges.Any(range => prior.StartOrdinal <= range.End && priorEnd >= range.Start))
            throw new ConflictException("One or more selected item quantities were already amended. Refresh the order.");
    }
}
