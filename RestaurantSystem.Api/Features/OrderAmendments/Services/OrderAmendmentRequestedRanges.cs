using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentRequestedRanges
{
    internal static (Dictionary<Guid, List<(int Start, int End)>> Ranges, HashSet<Guid> Instructions) Read(
        OrderAmendmentQuoteRequest request, IReadOnlyDictionary<Guid, int> itemQuantities)
    {
        var requested = new Dictionary<Guid, List<(int Start, int End)>>();
        var instructions = new HashSet<Guid>();
        foreach (var change in request.Changes)
        {
            if (!itemQuantities.TryGetValue(change.OrderItemId, out var quantity))
                throw new BadRequestException("Each amendment must reference a top-level source order item.");

            if (change.Kind == OrderAmendmentChangeKind.InstructionChange)
                AddInstruction(change.OrderItemId, quantity, requested, instructions);
            else
                AddRange(change, quantity, requested, instructions);
        }

        return (requested, instructions);
    }
    private static void AddInstruction(
        Guid itemId, int quantity, Dictionary<Guid, List<(int Start, int End)>> requested,
        HashSet<Guid> instructions)
    {
        if (requested.ContainsKey(itemId))
            throw new BadRequestException("An instruction change cannot share a source item with a quantity amendment.");
        if (!instructions.Add(itemId))
            throw new BadRequestException("An item can have only one instruction change in an amendment.");
        if (quantity <= 0)
            throw new BadRequestException("An empty source line cannot receive an instruction change.");
    }

    private static void AddRange(
        OrderAmendmentLineChangeRequest change, int quantity,
        Dictionary<Guid, List<(int Start, int End)>> requested, HashSet<Guid> instructions)
    {
        var end = (long)change.StartOrdinal + change.Quantity - 1;
        if (change.StartOrdinal < 1 || change.Quantity < 1 || end > quantity)
            throw new BadRequestException("The selected one-based item quantity range is outside the source line.");
        if (!requested.TryGetValue(change.OrderItemId, out var ranges))
        {
            ranges = [];
            requested.Add(change.OrderItemId, ranges);
        }
        if (instructions.Contains(change.OrderItemId))
            throw new BadRequestException("An instruction change cannot share a source item with a quantity amendment.");
        if (ranges.Any(range => change.StartOrdinal <= range.End && end >= range.Start))
            throw new BadRequestException("The same source item quantity cannot be amended twice in one request.");
        ranges.Add((change.StartOrdinal, (int)end));
    }

}
